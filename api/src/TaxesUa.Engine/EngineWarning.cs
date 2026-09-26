namespace TaxesUa.Engine;

/// <summary>
/// Something the caller must show the user. Each case carries its dates and no text: per ADR-002
/// the engine knows no locale, so the interface writes the sentence. The private constructor closes
/// the hierarchy, so the cases below are all there are.
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
}
