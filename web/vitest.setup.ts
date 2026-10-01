// The owner's days are Europe/Kyiv days. A runner in any other zone (a CI machine in UTC, a laptop
// elsewhere) must not be able to hide a date that shifts with the zone, so the tests run far from Kyiv.
process.env.TZ = "America/Los_Angeles";
