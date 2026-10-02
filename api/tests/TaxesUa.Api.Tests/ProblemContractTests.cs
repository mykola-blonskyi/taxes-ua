using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TaxesUa.Api.Tests;

// ADR-028: every failure carries a stable snake_case code, and the web translates by it.
public sealed class ProblemContractTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    private static string[] Codes() =>
    [
        .. typeof(ProblemCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!),
    ];

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "TaxesUa.Api", "Problems.cs")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("The api sources are not beside the tests."), "src", "TaxesUa.Api");
    }

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void Every_code_is_snake_case_and_none_repeats()
    {
        var codes = Codes();

        Assert.NotEmpty(codes);
        Assert.All(codes, code => Assert.Matches(SnakeCase, code));
        Assert.Equal(codes.Length, codes.Distinct().Count());
    }

    [Fact]
    public void Every_code_is_used_by_some_response()
    {
        var code = Sources().Where(path => !path.EndsWith("ProblemCodes.cs", StringComparison.Ordinal))
            .Select(File.ReadAllText).ToArray();
        var unused = typeof(ProblemCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.Name)
            .Where(name => !code.Any(text => text.Contains($"ProblemCodes.{name}", StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(unused);
    }

    [Fact]
    public void Nothing_writes_a_problem_except_the_one_helper()
    {
        var offenders = Sources()
            .Where(path => Path.GetRelativePath(SourceRoot(), path) != "Problems.cs")
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"\b(Results|TypedResults)\.(Problem|ValidationProblem)\b|new ProblemDetails|new HttpValidationProblemDetails|\.ProducesProblem\(|\.ProducesValidationProblem\("))
            .Select(path => Path.GetRelativePath(SourceRoot(), path))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public async Task A_missing_row_answers_a_404_problem_with_its_code()
    {
        using var owner = await ApiFixture.SignIn(fixture.CreateApplication(_ => { }), ApiFixture.AllowedEmail);

        var response = await owner.DeleteAsync($"/api/payments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ProblemAssert.CodeIsAsync(response, "payment_not_found");
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task A_rejected_query_names_the_code_of_each_field_beside_its_sentence()
    {
        using var owner = await ApiFixture.SignIn(fixture.CreateApplication(_ => { }), ApiFixture.AllowedEmail);

        var response = await owner.GetAsync("/api/payments?year=1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ProblemAssert.CodeIsAsync(response, "validation_failed");
        ProblemAssert.FieldIs(problem, "year", "year_out_of_range");
    }
}
