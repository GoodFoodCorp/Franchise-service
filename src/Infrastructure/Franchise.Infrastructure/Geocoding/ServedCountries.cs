namespace Franchise.Infrastructure.Geocoding;

/// <summary>Countries whose addresses are geocoded and suggested (ISO 3166-1
/// alpha-2, lower case). GEOCODER_COUNTRIES overrides the default: the 27
/// member states of the European Union.</summary>
public sealed class ServedCountries
{
    public static readonly IReadOnlyList<string> EuropeanUnion =
    [
        "at", "be", "bg", "hr", "cy", "cz", "dk", "ee", "fi", "fr", "de", "gr", "hu", "ie",
        "it", "lv", "lt", "lu", "mt", "nl", "pl", "pt", "ro", "sk", "si", "es", "se",
    ];

    public ServedCountries(IEnumerable<string> codes)
    {
        Codes = codes
            .Select(c => c.Trim().ToLowerInvariant())
            .Where(c => c.Length == 2 && c.All(char.IsAsciiLetterLower))
            .Distinct()
            .ToArray();
        if (Codes.Count == 0)
        {
            Codes = EuropeanUnion;
        }
    }

    public IReadOnlyList<string> Codes { get; }

    /// <summary>The Nominatim countrycodes parameter.</summary>
    public string AsQueryValue => string.Join(',', Codes);

    public bool Contains(string? code) => code is not null && Codes.Contains(code.ToLowerInvariant());

    public static ServedCountries FromSetting(string? setting) =>
        new(string.IsNullOrWhiteSpace(setting) ? EuropeanUnion : setting.Split(',', StringSplitOptions.RemoveEmptyEntries));
}
