using System.Reflection;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Tests.Architecture;

// Every "now" in our code comes from the injected TimeProvider, so a test's fake clock drives it. The
// top-level statements of Program register TimeProvider.System, the one place the real clock is named;
// the endpoint lambdas Program maps compile to other methods and get no exemption. Clocks read inside a
// package (MimeKit stamping an email's Date header, say) are not ours and are out of scope.
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

    // Main is async, so its body is the MoveNext of its state machine.
    private static bool IsComposition(MethodBase method) => method.DeclaringType is
        { Name: "<<Main>$>d__0", DeclaringType: { Name: "Program", DeclaringType: null } } && method.Name == "MoveNext";

    [Fact]
    public void Nothing_reads_the_clock_except_through_the_injected_TimeProvider()
    {
        Assembly[] assemblies = [typeof(AppDbContext).Assembly, Assembly.Load("TaxesUa.Engine")];

        var readers = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(CompiledReferences.InBodies)
            .Where(reference => !IsComposition(reference.Method))
            .Where(reference => reference.Reference is MethodBase { DeclaringType: { } declaring } called
                && AmbientClock.Contains((declaring, called.Name)))
            .Select(reference => $"{reference.Method.DeclaringType!.FullName}.{reference.Method.Name} reads {reference.Reference.DeclaringType!.Name}.{reference.Reference.Name[4..]}")
            .Distinct()
            .ToArray();

        Assert.True(readers.Length == 0, string.Join(Environment.NewLine, readers));
    }

    [Fact]
    public void Program_registers_the_real_clock()
    {
        var references = typeof(AppDbContext).Assembly.GetTypes().SelectMany(CompiledReferences.InBodies);

        Assert.Contains(references, reference => IsComposition(reference.Method)
            && reference.Reference is MethodBase { Name: "get_System" } called && called.DeclaringType == typeof(TimeProvider));
    }
}
