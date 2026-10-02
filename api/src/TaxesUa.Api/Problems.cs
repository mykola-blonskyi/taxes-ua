using Microsoft.AspNetCore.Mvc;

namespace TaxesUa.Api;

/// <summary>
/// The one place a failure response is written (ADR-028). Every ProblemDetails carries a stable machine
/// <c>code</c> from <see cref="ProblemCodes"/>; <c>title</c> and <c>detail</c> stay English sentences for
/// logs and the web never shows them. Nothing outside this file calls <c>Results.Problem</c> or
/// <c>Results.ValidationProblem</c> (a test enforces it).
/// </summary>
internal static class Problems
{
    public static IResult Create(
        int statusCode,
        string code,
        string title,
        string? detail = null,
        IDictionary<string, object?>? extensions = null)
    {
        var all = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code };
        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                all[key] = value;
            }
        }

        return Results.Problem(statusCode: statusCode, title: title, detail: detail, extensions: all);
    }

    /// <summary>
    /// A rejected request body. <c>errors</c> keeps the English sentences per field and <c>errorCodes</c>
    /// holds the code of each of them at the same index; the top-level <c>code</c> is
    /// <see cref="ProblemCodes.ValidationFailed"/>.
    /// </summary>
    public static IResult Validation(
        FieldErrors errors,
        string? title = null,
        int statusCode = StatusCodes.Status400BadRequest,
        string code = ProblemCodes.ValidationFailed) =>
        Results.ValidationProblem(
            errors.Messages(),
            title: title,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["errorCodes"] = errors.Codes(),
            });

    public static IResult Validation(string field, string code, string message, string? title = null)
    {
        var errors = new FieldErrors();
        errors.Set(field, code, message);

        return Validation(errors, title);
    }

    public static RouteHandlerBuilder ProducesCodedProblem(this RouteHandlerBuilder builder, int statusCode) =>
        builder.Produces<CodedProblemDetails>(statusCode, "application/problem+json");

    public static RouteHandlerBuilder ProducesFieldProblem(
        this RouteHandlerBuilder builder, int statusCode = StatusCodes.Status400BadRequest) =>
        builder.Produces<FieldProblemDetails>(statusCode, "application/problem+json");
}

/// <summary>The shape of a failure body in the OpenAPI document; the response itself is built by <see cref="Problems"/>.</summary>
public class CodedProblemDetails : ProblemDetails
{
    /// <summary>A stable snake_case name for the failure; the web translates by it.</summary>
    public required string Code { get; set; }
}

/// <summary>A rejected request body: <c>Errors</c> are English sentences, <c>ErrorCodes</c> their codes at the same index.</summary>
public class FieldProblemDetails : CodedProblemDetails
{
    public IDictionary<string, string[]> Errors { get; set; } = new Dictionary<string, string[]>();

    public IDictionary<string, string[]> ErrorCodes { get; set; } = new Dictionary<string, string[]>();
}

/// <summary>One reason a field was refused: a stable code, and the English sentence kept for logs.</summary>
internal sealed record Issue(string Code, string Message);

/// <summary>
/// The field errors of a request, keyed by camelCase field path. Setting a field replaces what it held,
/// as assigning into the dictionary this replaced did.
/// </summary>
internal sealed class FieldErrors
{
    private readonly Dictionary<string, Issue[]> byField = new(StringComparer.Ordinal);

    public int Count => byField.Count;

    public bool Has(string field) => byField.ContainsKey(field);

    public IEnumerable<string> Fields => byField.Keys;

    public Issue[] this[string field] => byField[field];

    public void Set(string field, string code, string message) => byField[field] = [new Issue(code, message)];

    public void Set(string field, Issue issue) => byField[field] = [issue];

    public void SetAll(string field, Issue[] issues) => byField[field] = issues;

    /// <summary>Adds a reason to the ones the field already has.</summary>
    public void Add(string field, string code, string message) =>
        byField[field] = byField.TryGetValue(field, out var held) ? [.. held, new Issue(code, message)] : [new Issue(code, message)];

    /// <summary>Takes every error of <paramref name="other"/> under <paramref name="prefix"/>, naming each field with <paramref name="rename"/> if given.</summary>
    public void Merge(string prefix, FieldErrors other, Func<string, string>? rename = null)
    {
        foreach (var (field, issues) in other.byField)
        {
            byField[$"{prefix}.{(rename is null ? field : rename(field))}"] = issues;
        }
    }

    public FieldErrors? OrNull() => byField.Count == 0 ? null : this;

    public Dictionary<string, string[]> Messages() =>
        byField.ToDictionary(pair => pair.Key, pair => pair.Value.Select(issue => issue.Message).ToArray(), StringComparer.Ordinal);

    public Dictionary<string, string[]> Codes() =>
        byField.ToDictionary(pair => pair.Key, pair => pair.Value.Select(issue => issue.Code).ToArray(), StringComparer.Ordinal);
}
