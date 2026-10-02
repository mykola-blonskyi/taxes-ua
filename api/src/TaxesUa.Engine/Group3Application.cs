namespace TaxesUa.Engine;

/// <summary>
/// The application for group 3 that keeps group 3 from the registration date (Tax Code 298.1.2). The
/// reminder plan and the home screen both read it here, so they cannot disagree on the date.
/// </summary>
public static class Group3Application
{
    /// <param name="registrationYearConfig">The parameters of the registration date's year.</param>
    public static DateOnly Deadline(DateOnly registrationDate, TaxYearConfigInput registrationYearConfig) =>
        registrationDate.AddDays(registrationYearConfig.Group3ApplicationDays);

    /// <summary>
    /// The deadline while the application is still the owner's to file: registered, no receipt
    /// confirmed, and group 3 expected from the registration date. Null otherwise; a later
    /// <c>Group3Start</c> means the window was missed and 298.1.4 applies instead. Compared through
    /// <c>Group3Start</c> rather than <c>Group3Since</c>, so a registration date edited after
    /// <c>Group3Since</c> was set to the old one still counts as group 3 from registration.
    /// </summary>
    public static DateOnly? Pending(FopSettingsInput settings, TaxYearConfigInput registrationYearConfig) =>
        settings.FopRegistrationDate is { } registered
        && !settings.Group3Confirmed
        && settings.Group3Start == registered
            ? Deadline(registered, registrationYearConfig)
            : null;
}
