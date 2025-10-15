using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using weather_report_smhi.Domain.Abstractions;
using weather_report_smhi.Domain.Constants;
using weather_report_smhi.Domain.Models;

namespace weather_report_smhi.Application;

public sealed class WeatherService : IWeatherService
{
    private static readonly TimeSpan LatestWindow = TimeSpan.FromMinutes(120);
    private readonly ISmhiClient _client;

    public WeatherService(ISmhiClient client) => _client = client;

    /// <summary>
    /// Returns Sweden average temperature for the latest hour if available.
    /// If that dataset contains no numeric samples, we fall back to per-station "latest-day"
    /// and keep only samples whose timestamps are within the last 120 minutes.
    /// </summary>
    public async Task<double?> GetSwedenAverageTemperatureLatestHourAsync(CancellationToken ct)
    {
        // --- Primary: station-set latest-hour (exists for parameter 1) ---
        var res = await _client.GetLatestHourTemperatureAllAsync(ct);

        var count = res?.Value?.Count ?? 0;

        var hourValues = res?.Value?
            .Where(v => v.Value.HasValue)
            .Select(v => v.Value!.Value)
            .ToArray() ?? Array.Empty<double>();

        if (hourValues.Length == 0 && count > 0)

        if (hourValues.Length > 0)
            return Math.Round(hourValues.Average(), 1, MidpointRounding.AwayFromZero);

        var stations = await _client.GetStationsAsync(MetObs.TemperatureParam, ct);
        var list = stations?.Station ?? Array.Empty<StationInfo>();
        if (list.Count == 0) return null; // <-- Count, not Length

        const int maxConcurrency = 6; // keep gentle
        using var sem = new SemaphoreSlim(maxConcurrency);
        var bag = new ConcurrentBag<double>();
        var now = DateTimeOffset.UtcNow;

        var tasks = list.Select(async s =>
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var series = await _client.GetLatestDayTemperatureForStationAsync(s.Id, ct).ConfigureAwait(false);
                var recent = series?.Value?
                    .Where(v => v.Value.HasValue)
                    .Select(v => new { v.Value, Dt = FromUnixMs(v.DateUnixMs) })
                    .Where(x => x.Dt.HasValue && (now - x.Dt!.Value) <= LatestWindow)
                    .OrderByDescending(x => x.Dt!.Value)
                    .Select(x => (double?)x.Value!.Value)
                    .FirstOrDefault();

                if (recent.HasValue) bag.Add(recent.Value);
            }
            catch
            {
                // swallow per-station failures; best-effort average
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (bag.IsEmpty) return null;

        var avg = bag.Average();
        return Math.Round(avg, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Sums monthly precipitation (mm) for Lund (station 53430) over the “latest-months” period,
    /// and returns both the total and the list of month labels like "2025-06".
    /// </summary>
    public async Task<(double totalMm, IReadOnlyList<string> months)> GetLundTotalRainLatestMonthsAsync(CancellationToken ct)
    {
        const int lundStationId = 53430;
        var data = await _client.GetLatestMonthsForStationAsync(MetObs.MonthlyPrecipParam, lundStationId, ct);

        var items = data?.Value ?? Array.Empty<StationData>();
        if (items.Count == 0) return (0d, Array.Empty<string>());

        var months = new List<string>(items.Count);
        double sum = 0;

        foreach (var v in items)
        {
            if (v.Value.HasValue) sum += v.Value.Value;

            string label =
                !string.IsNullOrWhiteSpace(v.Ref) ? v.Ref! :
                v.From.HasValue ? FromUnixMs(v.From.Value)?.ToString("yyyy-MM") ?? "" :
                "";

            if (!string.IsNullOrEmpty(label))
                months.Add(label);
        }

        return (Math.Round(sum, 1, MidpointRounding.AwayFromZero), months);
    }

    /// <summary>
    /// Streams the most recent temperature for each station (per-station "latest-day", throttled).
    /// Each yielded item is (stationId, stationName, tempC). Some stations may yield null tempC.
    /// </summary>
    public async IAsyncEnumerable<(int stationId, string stationName, double? tempC)> StreamAllStationsTemperatureAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var stations = await _client.GetStationsAsync(MetObs.TemperatureParam, ct);
        var list = stations?.Station ?? Array.Empty<StationInfo>();

        const int maxConcurrency = 6;
        using var sem = new SemaphoreSlim(maxConcurrency);
        var bag = new ConcurrentBag<(int id, string name, double? t)>();

        var tasks = list.Select(async s =>
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var series = await _client.GetLatestDayTemperatureForStationAsync(s.Id, ct).ConfigureAwait(false);
                var latest = series?.Value?
                    .Where(v => v.Value.HasValue)
                    .Select(v => new { v.Value, Dt = FromUnixMs(v.DateUnixMs) })
                    .OrderByDescending(x => x.Dt ?? DateTimeOffset.MinValue)
                    .Select(x => (double?)x.Value!.Value)
                    .FirstOrDefault();

                bag.Add((s.Id, s.Name, latest));
            }
            catch
            {
                bag.Add((s.Id, s.Name, null));
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var item in bag)
            yield return item;
    }

    // ---- helpers ----
    private static DateTimeOffset? FromUnixMs(long ms)
    {
        if (ms <= 0) return null;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(ms); }
        catch { return null; }
    }
}
