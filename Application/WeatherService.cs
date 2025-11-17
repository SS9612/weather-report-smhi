using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using weather_report_smhi.Domain.Abstractions;
using weather_report_smhi.Domain.Constants;
using weather_report_smhi.Domain.Models;

namespace weather_report_smhi.Application;

public sealed class WeatherService : IWeatherService
{
    private static readonly TimeSpan LatestWindow = TimeSpan.FromMinutes(120);
    private readonly ISmhiClient _client;
    private readonly ILogger<WeatherService> _logger;

    public WeatherService(ISmhiClient client, ILogger<WeatherService> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Returns Sweden average temperature for the latest hour if available.
    /// </summary>
    public async Task<double?> GetSwedenAverageTemperatureLatestHourAsync(CancellationToken ct)
    {
        // --- Primary: station-set latest-hour (exists for parameter 1) ---
        var res = await _client.GetLatestHourTemperatureAllAsync(ct).ConfigureAwait(false);

        var count = res?.Value?.Count ?? 0;

        var hourValues = res?.Value?
            .Where(v => v.Value.HasValue)
            .Select(v => v.Value!.Value)
            .ToArray() ?? Array.Empty<double>();

        if (hourValues.Length > 0)
            return Math.Round(hourValues.Average(), 1, MidpointRounding.AwayFromZero);

        var stations = await _client.GetStationsAsync(MetObs.TemperatureParam, ct).ConfigureAwait(false);
        var list = stations?.Station ?? Array.Empty<StationInfo>();
        if (list.Count == 0) return null;

        const int maxConcurrency = 6;
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch temperature for station {StationId}", s.Id);
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
    /// </summary>
    public async Task<(double totalMm, IReadOnlyList<string> months)> GetLundTotalRainLatestMonthsAsync(CancellationToken ct)
    {
        const int lundStationId = 53430;
        var data = await _client.GetLatestMonthsForStationAsync(MetObs.MonthlyPrecipParam, lundStationId, ct).ConfigureAwait(false);

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
        var stations = await _client.GetStationsAsync(MetObs.TemperatureParam, ct).ConfigureAwait(false);
        var list = stations?.Station ?? Array.Empty<StationInfo>();

        const int maxConcurrency = 6;
        using var sem = new SemaphoreSlim(maxConcurrency);
        var channel = System.Threading.Channels.Channel.CreateBounded<(int id, string name, double? t)>(
            new System.Threading.Channels.BoundedChannelOptions(64)
            {
                SingleReader = true,
                SingleWriter = false
            });

        // Start all tasks, but don't wait for them
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

                await channel.Writer.WriteAsync((s.Id, s.Name, latest), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch temperature for station {StationId}", s.Id);
                try
                {
                    await channel.Writer.WriteAsync((s.Id, s.Name, null), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Channel closed or cancelled - ignore
                }
            }
            finally
            {
                sem.Release();
            }
        }).ToList();

        // Close the channel when all tasks complete (don't pass ct to ensure it always completes)
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.Complete();
            }
        });

        // Stream results as they become available
        await foreach (var item in channel.Reader.ReadAllAsync(ct))
        {
            // Skip stations with null temperature or invalid station names
            if (!item.t.HasValue || 
                string.IsNullOrWhiteSpace(item.name) || 
                item.name.Equals("n/a", StringComparison.OrdinalIgnoreCase))
                continue;
            
            yield return item;
        }
    }

    // ---- helpers ----
    private static DateTimeOffset? FromUnixMs(long ms)
    {
        if (ms <= 0) return null;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(ms); }
        catch { return null; }
    }
}
