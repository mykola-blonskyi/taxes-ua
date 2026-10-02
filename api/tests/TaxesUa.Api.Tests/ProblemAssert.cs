using System.Net.Http.Json;
using System.Text.Json;

namespace TaxesUa.Api.Tests;

/// <summary>
/// What a test asks of a failure body (ADR-028): its stable <c>code</c>, and the <c>errorCodes</c> of the
/// field it expects rejected. The English <c>title</c>, <c>detail</c> and <c>errors</c> are for logs and
/// are not asserted on.
/// </summary>
internal static class ProblemAssert
{
    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>The problem's code, which a rejected body reports as <c>validation_failed</c> unless it names a more specific one.</summary>
    public static async Task<JsonElement> CodeIsAsync(HttpResponseMessage response, string code)
    {
        var problem = await ReadAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        return problem;
    }

    /// <summary>The field is rejected with exactly these codes, each with its English sentence at the same index.</summary>
    public static void FieldIs(JsonElement problem, string field, params string[] codes)
    {
        Assert.Equal(codes, problem.GetProperty("errorCodes").GetProperty(field).EnumerateArray().Select(code => code.GetString()));
        Assert.Equal(codes.Length, problem.GetProperty("errors").GetProperty(field).GetArrayLength());
    }

    /// <summary>The body rejects <paramref name="field"/>: it has codes under it, in snake_case, each with its sentence.</summary>
    public static void Rejects(JsonElement problem, string field)
    {
        Assert.True(problem.GetProperty("errorCodes").TryGetProperty(field, out var codes), $"no code under {field}");
        Assert.NotEmpty(codes.EnumerateArray());
        Assert.All(codes.EnumerateArray(), code => Assert.Matches("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", code.GetString()!));
        Assert.Equal(codes.GetArrayLength(), problem.GetProperty("errors").GetProperty(field).GetArrayLength());
    }

    public static string[] FieldsOf(JsonElement problem) =>
        [.. problem.GetProperty("errorCodes").EnumerateObject().Select(property => property.Name)];

    public static string[] CodesOf(JsonElement problem, string field) =>
        [.. problem.GetProperty("errorCodes").GetProperty(field).EnumerateArray().Select(code => code.GetString()!)];
}
