using System.Globalization;

namespace Hsm.Application;

/// <summary>
/// The ISO-8601 serialization used everywhere a timestamp crosses the API
/// surface (envelope metadata, entity JSON, JWT onboarding claim):
/// millisecond precision, UTC, trailing 'Z'. The format string is contract —
/// integration consumers parse it.
/// </summary>
public static class IsoTimestamp
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public static string Of(DateTimeOffset value) =>
        value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);

    public static string? Of(DateTimeOffset? value) =>
        value is { } present ? Of(present) : null;
}
