using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TaxesUa.Api;

/// <summary>
/// Binds an enum only from a string naming exactly one of its members, case-insensitively. Replaces
/// <see cref="JsonStringEnumConverter"/> everywhere: <c>Enum.TryParse</c> accepts a comma-separated
/// list of member names for any enum, flags or not, and ORs their values together, so
/// <c>"Income, RefundToClient"</c> parses as the single value of whichever name that combination
/// happens to equal (here, <c>RefundToClient</c>) instead of failing. It also parses a digit string such
/// as <c>"1"</c> as that ordinal and trims surrounding whitespace, so both bind to a real member
/// silently. A caller cannot see any of that from the response. This converter therefore never calls
/// <c>Enum.TryParse</c>: it looks the string up among the declared member names, and rejects anything
/// else, including a JSON number (as the replaced converter's <c>allowIntegerValues: false</c> did).
/// </summary>
internal sealed class StrictEnumJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(StrictEnumJsonConverter<>).MakeGenericType(typeToConvert))!;
}

internal sealed class StrictEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"{typeToConvert.Name} must be a string naming one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
        }

        var value = reader.GetString()!;
        if (!ByName.TryGetValue(value, out var result))
        {
            throw new JsonException(
                $"\"{value}\" is not a member of {typeToConvert.Name}. Expected one of: "
                + $"{string.Join(", ", Enum.GetNames<TEnum>())}.");
        }

        return result;
    }

    private static readonly Dictionary<string, TEnum> ByName =
        Enum.GetNames<TEnum>().ToDictionary(n => n, Enum.Parse<TEnum>, StringComparer.OrdinalIgnoreCase);

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>
/// Microsoft.AspNetCore.OpenApi's schema generator special-cases <see cref="JsonStringEnumConverter"/>
/// to describe an enum as its member names; a custom <see cref="JsonConverter{T}"/> like
/// <see cref="StrictEnumJsonConverter{TEnum}"/> gets none of that. System.Text.Json's
/// <c>JsonSchemaExporter</c> falls back further still for an array of such an enum (e.g.
/// <c>DayOfWeek[] WeekendDays</c>): it emits no <c>items</c> schema at all rather than an empty one, so
/// that case is fixed here too, not just a bare enum property. This restores the same
/// string-with-named-values shape web/'s generated client (`pnpm gen:api`) already expects.
/// </summary>
internal sealed class EnumSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (Nullable.GetUnderlyingType(type) is { IsEnum: true } nullableEnumType)
        {
            // A nullable value type has no JsonTypeInfo of its own: the exporter calls this with the
            // Nullable<TEnum> wrapper itself, which IsEnum reports false for, so it is unwrapped here.
            ApplyEnum(schema, nullableEnumType, nullable: true);
        }
        else if (type.IsEnum)
        {
            ApplyEnum(schema, type, nullable: false);
        }
        else if (type.IsArray && type.GetElementType() is { IsEnum: true } elementType)
        {
            var items = new OpenApiSchema();
            ApplyEnum(items, elementType, nullable: false);
            schema.Items = items;
        }

        return Task.CompletedTask;
    }

    private static void ApplyEnum(OpenApiSchema schema, Type enumType, bool nullable)
    {
        schema.Type = nullable ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
        schema.Enum = [.. Enum.GetNames(enumType).Select(name => (JsonNode)JsonValue.Create(name))];
        if (nullable)
        {
            // A JSON null in a JsonNode-based tree, such as this Enum list, is the null reference
            // itself; there is no JsonValue instance that represents it.
            schema.Enum.Add(null!);
        }
    }
}
