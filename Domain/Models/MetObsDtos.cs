using System.Text.Json.Serialization;

namespace weather_report_smhi.Domain.Models;

public sealed record StationSetDataResponse(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] IReadOnlyList<StationData> Value
);

public sealed record StationData(
    [property: JsonPropertyName("date")] long DateUnixMs,
    [property: JsonPropertyName("value")] double? Value,
    [property: JsonPropertyName("quality")] int? Quality,
    [property: JsonPropertyName("station")] int? StationId
);

public sealed record StationSetResponse(
    [property: JsonPropertyName("station")] IReadOnlyList<StationInfo> Station
);

public sealed record StationInfo(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name
);
