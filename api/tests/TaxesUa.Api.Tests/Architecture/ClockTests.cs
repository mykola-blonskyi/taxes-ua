using System.Reflection;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Tests.Architecture;

// Every "now" comes from the injected TimeProvider, so a test's fake clock drives it. Program registers
// TimeProvider.System, the one place the real clock is named.
public sealed class ClockTests
{
    private static readonly HashSet<(Type, string)> AmbientClock =
    [
        (typeof(DateTime), "get_Now"),
        (typeof(DateTime), "get_UtcNow"),
        (typeof(DateTime), "get_Today"),
        (typeof(DateTimeOffset), "get_Now"),
        (typeof(DateTimeOffset), "get_UtcNow"),
        (typeof(TimeProvider), "get_System"),
    ];

    private static bool IsProgramWiring(Type type) => type.FullName!.StartsWith("Program", StringComparison.Ordinal);

    [Fact]
    public void Nothing_reads_the_clock_except_through_the_injected_TimeProvider()
    {
        Assembly[] assemblies = [typeof(AppDbContext).Assembly, Assembly.Load("TaxesUa.Engine")];

        var readers = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !IsProgramWiring(type))
            .SelectMany(type => CompiledReferences.Of(type)
                .OfType<MethodBase>()
                .Where(method => method.DeclaringType is { } declaring && AmbientClock.Contains((declaring, method.Name)))
                .Select(method => $"{type.FullName} reads {method.DeclaringType!.Name}.{method.Name[4..]}"))
            .Distinct()
            .ToArray();

        Assert.True(readers.Length == 0, string.Join(Environment.NewLine, readers));
    }
}
