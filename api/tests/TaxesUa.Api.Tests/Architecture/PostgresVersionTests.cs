using System.Text.RegularExpressions;

namespace TaxesUa.Api.Tests.Architecture;

// Tests, CI and the local stack ran Postgres 16 for a week while production ran 18, and nothing
// noticed (#247). Every image tag, client package and setup line in the repository names one major
// version. Reports, ticket mirrors and the ADR log are history and may name an older one.
public sealed partial class PostgresVersionTests
{
    private static readonly string[] SkippedDirectories =
        [".git", ".claude", ".scratch", "node_modules", "bin", "obj", ".next", "reports"];

    private static readonly string[] SkippedFiles = [Path.Combine("docs", "decisions.md")];

    [Fact]
    public void The_repository_names_one_Postgres_major_version()
    {
        var root = RepositoryRoot();

        var mentions = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)
                .Any(part => SkippedDirectories.Contains(part)))
            .Where(path => !SkippedFiles.Contains(Path.GetRelativePath(root, path)))
            .Where(path => Path.GetExtension(path) is ".yml" or ".yaml" or ".md" or ".cs" or ".ts" or ".sh" or ""
                || Path.GetFileName(path) == "Dockerfile")
            .SelectMany(path => VersionPattern().Matches(File.ReadAllText(path))
                .Select(match => (Path: Path.GetRelativePath(root, path), Major: match.Groups["major"].Value)))
            .ToArray();

        Assert.NotEmpty(mentions);
        var majors = mentions.Select(mention => mention.Major).Distinct().ToArray();
        Assert.True(
            majors.Length == 1,
            "Postgres majors differ across the repository:\n"
                + string.Join(Environment.NewLine, mentions.Select(mention => $"{mention.Path}: {mention.Major}").Distinct()));
    }

    [GeneratedRegex(@"(?:postgres:|postgresql-client-|postgresql@|PostgreSQL )(?<major>\d{2})\b")]
    private static partial Regex VersionPattern();

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("docker-compose.yml is not above the test binaries.");
    }
}
