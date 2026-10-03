using System.Reflection;
using System.Reflection.Emit;

namespace TaxesUa.Api.Tests.Architecture;

/// <summary>
/// What the compiled code of a type names: its base and interfaces, attributes, member signatures, locals,
/// and every type, method and field token in its method bodies. Lambdas, async state machines and
/// iterators compile to nested types, and <see cref="Type.Namespace"/> of a nested type is its outer
/// type's, so their references count against the type that wrote them.
/// </summary>
internal static class CompiledReferences
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<int, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => (int)(ushort)code.Value);

    public static IEnumerable<MemberInfo> Of(Type type)
    {
        if (type.BaseType is { } baseType)
        {
            yield return baseType;
        }

        foreach (var member in type.GetInterfaces())
        {
            yield return member;
        }

        foreach (var attribute in type.GetCustomAttributesData())
        {
            yield return attribute.AttributeType;
        }

        foreach (var field in type.GetFields(Declared))
        {
            yield return field.FieldType;
        }

        foreach (var property in type.GetProperties(Declared))
        {
            yield return property.PropertyType;
        }

        foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
        {
            if (method is MethodInfo { ReturnType: var returnType })
            {
                yield return returnType;
            }

            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }

            foreach (var reference in InBody(method))
            {
                yield return reference;
            }
        }
    }

    /// <summary>A referenced member and the types it carries, with generic arguments and element types unwrapped.</summary>
    public static IEnumerable<Type> Types(MemberInfo member) => member switch
    {
        Type type => Unwrap(type),
        MethodBase method => Unwrap(method.DeclaringType)
            .Concat(method is MethodInfo info ? Unwrap(info.ReturnType) : [])
            .Concat(method.GetParameters().SelectMany(parameter => Unwrap(parameter.ParameterType)))
            .Concat(method.IsGenericMethod ? method.GetGenericArguments().SelectMany(Unwrap) : []),
        FieldInfo field => Unwrap(field.DeclaringType).Concat(Unwrap(field.FieldType)),
        _ => [],
    };

    private static IEnumerable<Type> Unwrap(Type? type)
    {
        if (type is null || type.IsGenericParameter)
        {
            return [];
        }

        if (type.HasElementType)
        {
            return Unwrap(type.GetElementType());
        }

        return type.IsConstructedGenericType
            ? [type.GetGenericTypeDefinition(), .. type.GetGenericArguments().SelectMany(Unwrap)]
            : [type];
    }

    private static IEnumerable<MemberInfo> InBody(MethodBase method)
    {
        var body = method.GetMethodBody();
        if (body is null)
        {
            yield break;
        }

        foreach (var local in body.LocalVariables)
        {
            yield return local.LocalType;
        }

        var il = body.GetILAsByteArray() ?? [];
        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var at = 0;
        while (at < il.Length)
        {
            var value = il[at] == 0xFE ? 0xFE00 | il[at + 1] : il[at];
            at += value > 0xFF ? 2 : 1;
            var code = OpCodesByValue[value];
            switch (code.OperandType)
            {
                case OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok:
                    yield return method.Module.ResolveMember(BitConverter.ToInt32(il, at), typeArguments, methodArguments)!;
                    at += 4;
                    break;
                case OperandType.InlineSwitch:
                    at += 4 + (4 * BitConverter.ToInt32(il, at));
                    break;
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    at += 1;
                    break;
                case OperandType.InlineVar:
                    at += 2;
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    at += 8;
                    break;
                default:
                    at += 4;
                    break;
            }
        }
    }
}
