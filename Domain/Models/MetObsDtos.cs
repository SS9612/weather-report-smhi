using System.Text.Json.Serialization;
using weather_report_smhi.Infrastructure.Seralization;

namespace weather_report_smhi.Domain.Models;

public sealed record StationSetDataResponse(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] IReadOnlyList<StationData> Value
);

public sealed record StationData(
    // Sometimes epoch seconds, sometimes milliseconds, sometimes 0 for monthly.
    [property: JsonPropertyName("date")] long DateUnixMs,

    // Value may be number or string ("0.0", "0,0", "NaN").
    [property: JsonPropertyName("value"), JsonConverter(typeof(FlexibleDoubleConverter))] double? Value,

    // Quality is sometimes a code string ("G") — we don't use it.
    [property: JsonPropertyName("quality")] string? Quality,

    // Station id is present on station-set datasets.
    [property: JsonPropertyName("station")] int? StationId,

    // ---- Monthly-specific fallbacks ----
    [property: JsonPropertyName("ref")] string? Ref,

    // Sometimes provided instead of 'date' for periods.
    [property: JsonPropertyName("from")] long? From,

    [property: JsonPropertyName("to")] long? To
);

public sealed record StationSetResponse(
    [property: JsonPropertyName("station")] IReadOnlyList<StationInfo> Station
);

public sealed record StationInfo(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("link")] IReadOnlyList<SmhiLink>? Link
);

public sealed record SmhiLink(
    [property: JsonPropertyName("rel")] string Rel,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("href")] string Href
);
