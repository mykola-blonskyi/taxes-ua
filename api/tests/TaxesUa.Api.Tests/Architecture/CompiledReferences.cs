using System.Reflection;
using System.Reflection.Emit;

namespace TaxesUa.Api.Tests.Architecture;

/// <summary>
/// What the compiled code of a type names: its base and interfaces, generic constraints, member signatures,
/// the attributes on the type and its members (with their typeof and enum arguments), locals, and every
/// type, method and field token in its method bodies. A const or enum value read is inlined as a number,
/// so it is not here; FeatureBoundaryTests scans the sources for those. Lambdas, async state machines and
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
        IEnumerable<MemberInfo> declaration =
        [
            .. type.BaseType is { } baseType ? [baseType] : Array.Empty<Type>(),
            .. type.GetInterfaces(),
            .. Constraints(type.IsGenericTypeDefinition ? type.GetGenericArguments() : []),
            .. Attributes(type.GetCustomAttributesData()),
        ];
        var fields = type.GetFields(Declared)
            .SelectMany(field => Attributes(field.GetCustomAttributesData()).Prepend(field.FieldType));
        var properties = type.GetProperties(Declared)
            .SelectMany(property => Attributes(property.GetCustomAttributesData()).Prepend(property.PropertyType));
        var methods = type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared))
            .SelectMany(method => Signature(method).Concat(Bodies(method).Select(reference => reference.Reference)));

        return declaration.Concat(fields).Concat(properties).Concat(methods);
    }

    /// <summary>Each member a method body of <paramref name="type"/> names, with the method that names it.</summary>
    public static IEnumerable<(MethodBase Method, MemberInfo Reference)> InBodies(Type type) =>
        type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)).SelectMany(Bodies);

    private static IEnumerable<MemberInfo> Signature(MethodBase method) =>
    [
        .. method is MethodInfo info ? [info.ReturnType, .. Attributes(info.ReturnParameter.GetCustomAttributesData())] : Array.Empty<MemberInfo>(),
        .. Attributes(method.GetCustomAttributesData()),
        .. Constraints(method.IsGenericMethodDefinition ? method.GetGenericArguments() : []),
        .. method.GetParameters().SelectMany(parameter => Attributes(parameter.GetCustomAttributesData()).Prepend(parameter.ParameterType)),
    ];

    private static IEnumerable<Type> Constraints(Type[] parameters) =>
        parameters.SelectMany(parameter => parameter.GetGenericParameterConstraints());

    // An attribute names its own type, and the types of its arguments, including typeof(...) and enum values.
    private static IEnumerable<Type> Attributes(IEnumerable<CustomAttributeData> attributes) =>
        attributes.SelectMany(attribute => attribute.ConstructorArguments
            .Concat(attribute.NamedArguments.Select(named => named.TypedValue))
            .SelectMany(Argument)
            .Prepend(attribute.AttributeType));

    private static IEnumerable<Type> Argument(CustomAttributeTypedArgument argument) => argument.Value switch
    {
        Type value => [argument.ArgumentType, value],
        IEnumerable<CustomAttributeTypedArgument> items => items.SelectMany(Argument).Prepend(argument.ArgumentType),
        _ => [argument.ArgumentType],
    };

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

    private static IEnumerable<(MethodBase Method, MemberInfo Reference)> Bodies(MethodBase method)
    {
        var body = method.GetMethodBody();
        if (body is null)
        {
            yield break;
        }

        foreach (var local in body.LocalVariables)
        {
            yield return (method, local.LocalType);
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
                    yield return (method, method.Module.ResolveMember(BitConverter.ToInt32(il, at), typeArguments, methodArguments)!);
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
