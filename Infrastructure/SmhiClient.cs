using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using weather_report_smhi.Domain.Abstractions;
using weather_report_smhi.Domain.Constants;
using weather_report_smhi.Domain.Models;

namespace weather_report_smhi.Infrastructure;

public sealed class SmhiClient(HttpClient http) : ISmhiClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Task<StationSetDataResponse?> GetLatestHourTemperatureAllAsync(CancellationToken ct) =>
        http.GetFromJsonAsync<StationSetDataResponse>(
            $"/api/version/1.0/parameter/{MetObs.TemperatureParam}/station-set/all/period/latest-hour/data.json", Json, ct);

    public Task<StationSetResponse?> GetStationsAsync(int parameterId, CancellationToken ct) =>
        http.GetFromJsonAsync<StationSetResponse>(
            $"/api/version/1.0/parameter/{parameterId}/station-set/all.json", Json, ct);

    public Task<StationSetDataResponse?> GetLatestMonthsForStationAsync(int parameterId, int stationId, CancellationToken ct) =>
        http.GetFromJsonAsync<StationSetDataResponse>(
            $"/api/version/1.0/parameter/{parameterId}/station/{stationId}/period/latest-months/data.json", Json, ct);
}
