using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using weather_report_smhi.Domain.Abstractions;
using weather_report_smhi.Domain.Constants;
using weather_report_smhi.Domain.Models;
using weather_report_smhi.Infrastructure.Seralization;

namespace weather_report_smhi.Infrastructure;

public sealed class SmhiClient(HttpClient http) : ISmhiClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Stations that have proven to NOT expose latest-day for temperature (404 once => skip later)
    private static readonly ConcurrentDictionary<int, byte> _noLatestDayTempStation = new();

    static SmhiClient()
    {
        // Accept numbers OR strings for double? values from SMHI datasets.
        _jsonOptions.Converters.Add(new FlexibleDoubleConverter());
    }

    public async Task<StationSetDataResponse?> GetLatestHourTemperatureAllAsync(CancellationToken ct)
    {
        var url = $"/api/version/1.0/parameter/{MetObs.TemperatureParam}/station-set/all/period/latest-hour/data.json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (resp.StatusCode == HttpStatusCode.NotFound) return null; // quiet 404
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"[http] {resp.StatusCode} for {url}");
            return null;
        }

        try { return await resp.Content.ReadFromJsonAsync<StationSetDataResponse>(_jsonOptions, ct); }
        catch (Exception ex) { Console.Error.WriteLine($"[http] JSON parse failed for {url}: {ex.Message}"); return null; }
    }

    public async Task<StationSetResponse?> GetStationsAsync(int parameterId, CancellationToken ct)
    {
        var url = $"/api/version/1.0/parameter/{parameterId}.json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (resp.StatusCode == HttpStatusCode.NotFound) return null; // quiet 404
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"[http] {resp.StatusCode} for {url}");
            return null;
        }

        try { return await resp.Content.ReadFromJsonAsync<StationSetResponse>(_jsonOptions, ct); }
        catch (Exception ex) { Console.Error.WriteLine($"[http] JSON parse failed for {url}: {ex.Message}"); return null; }
    }

    public async Task<StationSetDataResponse?> GetLatestMonthsForStationAsync(int parameterId, int stationId, CancellationToken ct)
    {
        var url = $"/api/version/1.0/parameter/{parameterId}/station/{stationId}/period/latest-months/data.json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (resp.StatusCode == HttpStatusCode.NotFound) return null; // quiet 404
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"[http] {resp.StatusCode} for {url}");
            return null;
        }

        try { return await resp.Content.ReadFromJsonAsync<StationSetDataResponse>(_jsonOptions, ct); }
        catch (Exception ex) { Console.Error.WriteLine($"[http] JSON parse failed for {url}: {ex.Message}"); return null; }
    }

    public async Task<StationSetDataResponse?> GetLatestDayTemperatureAllAsync(CancellationToken ct)
    {
        var url = $"/api/version/1.0/parameter/{MetObs.TemperatureParam}/station-set/all/period/latest-day/data.json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (resp.StatusCode == HttpStatusCode.NotFound) return null; // quiet 404
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"[http] {resp.StatusCode} for {url}");
            return null;
        }

        try { return await resp.Content.ReadFromJsonAsync<StationSetDataResponse>(_jsonOptions, ct); }
        catch (Exception ex) { Console.Error.WriteLine($"[http] JSON parse failed for {url}: {ex.Message}"); return null; }
    }

    public async Task<StationSetDataResponse?> GetLatestDayTemperatureForStationAsync(int stationId, CancellationToken ct)
    {
        // If we already saw a 404 for this station/period once, skip calling again to avoid spam + wasted calls
        if (_noLatestDayTempStation.ContainsKey(stationId))
            return null;

        var url = $"/api/version/1.0/parameter/{MetObs.TemperatureParam}/station/{stationId}/period/latest-day/data.json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            _noLatestDayTempStation.TryAdd(stationId, 0); // memoize missing period for this station
            return null; // quiet 404
        }

        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"[http] {resp.StatusCode} for {url}");
            return null;
        }

        try { return await resp.Content.ReadFromJsonAsync<StationSetDataResponse>(_jsonOptions, ct); }
        catch (Exception ex) { Console.Error.WriteLine($"[http] JSON parse failed for {url}: {ex.Message}"); return null; }
    }
}
