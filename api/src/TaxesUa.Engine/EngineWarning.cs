namespace TaxesUa.Engine;

/// <summary>
/// Something the caller must show the user. Each case carries its dates and no text: per ADR-002
/// the engine knows no locale, so the interface writes the sentence. The private constructor stops
/// direct construction outside the cases below, but does not close the hierarchy: C# requires a
/// non-sealed record's copy constructor to be at least <c>protected</c>, so another assembly can
/// still declare a further case. A switch over the cases below is exhaustive by convention, not by
/// the compiler.
/// </summary>
public abstract record EngineWarning
{
    private EngineWarning() { }

    /// <summary>
    /// Rule 8: the operation predates the FOP, so it is excluded from income rather than dropped in
    /// silence.
    /// </summary>
    public sealed record OperationBeforeRegistration(
        DateOnly ValueDate,
        DateOnly FopRegistrationDate) : EngineWarning;

    /// <summary>
    /// Rule 8: without a registration date there are no obligations at all, which is why every
    /// figure of the year comes back zero.
    /// </summary>
    public sealed record FopRegistrationDateNotSet : EngineWarning;

    /// <summary>
    /// The cumulative single tax or military levy for a quarter came out negative, typically a
    /// refund that shrank income already taxed in an earlier period. The figure is correct; Rule 7
    /// has nothing to net it against, so the caller has to explain it rather than pass it through.
    /// </summary>
    public sealed record NegativeCumulativeTax(int Quarter) : EngineWarning;
}
