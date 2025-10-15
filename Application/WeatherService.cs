using System.Runtime.CompilerServices;
using weather_report_smhi.Domain.Abstractions;
using weather_report_smhi.Domain.Constants;
using weather_report_smhi.Domain.Models;

namespace weather_report_smhi.Application;

/// <summary>
/// Business logic. Keeps Program.cs minimal and respects LoD.
/// </summary>
public sealed class WeatherService(ISmhiClient client) : IWeatherService
{
    /// <summary>
    /// Computes the average temperature over all stations for the latest hour.
    /// Returns null if no numeric values are available.
    /// </summary>
    public async Task<double?> GetSwedenAverageTemperatureLatestHourAsync(CancellationToken ct)
    {
        var res = await client.GetLatestHourTemperatureAllAsync(ct);
        var values = res?.Value?.Where(v => v.Value.HasValue).Select(v => v.Value!.Value).ToArray() ?? [];
        return values.Length == 0 ? null : Math.Round(values.Average(), 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Sums monthly precipitation (mm) for a station whose name contains "Lund".
    /// Returns (0, empty) if station or values are not found.
    /// </summary>
    public async Task<(double totalMm, IReadOnlyList<string> months)> GetLundTotalRainLatestMonthsAsync(CancellationToken ct)
    {
        var stations = await client.GetStationsAsync(MetObs.MonthlyPrecipParam, ct);
        var lund = stations?.Station?.FirstOrDefault(s => s.Name?.Contains("Lund", StringComparison.OrdinalIgnoreCase) == true);
        if (lund is null) return (0, Array.Empty<string>());

        var data = await client.GetLatestMonthsForStationAsync(MetObs.MonthlyPrecipParam, lund.Id, ct);
        var items = data?.Value?.Where(v => v.Value.HasValue) ?? Enumerable.Empty<StationData>();

        var byMonth = items
            .GroupBy(v => DateTimeOffset.FromUnixTimeMilliseconds(v.DateUnixMs).UtcDateTime)
            .Select(g => new { Month = new DateTime(g.Key.Year, g.Key.Month, 1), Sum = g.Sum(x => x.Value ?? 0) })
            .OrderBy(x => x.Month)
            .ToList();

        var total = Math.Round(byMonth.Sum(x => x.Sum), 1);
        var labels = byMonth.Select(x => x.Month.ToString("yyyy-MM")).ToList();
        return (total, labels);
    }

    /// <summary>
    /// Streams temperature readings with a 100ms pause and cancellation support.
    /// </summary>
    public async IAsyncEnumerable<(int stationId, string stationName, double? tempC)> StreamAllStationsTemperatureAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var latest = await client.GetLatestHourTemperatureAllAsync(ct);
        var stationSet = await client.GetStationsAsync(MetObs.TemperatureParam, ct);
        var names = (stationSet?.Station ?? []).ToDictionary(s => s.Id, s => s.Name ?? s.Id.ToString());

        foreach (var v in latest?.Value ?? [])
        {
            ct.ThrowIfCancellationRequested();
            var name = v.StationId is int id && names.TryGetValue(id, out var n) ? n : $"Station {v.StationId}";
            yield return (v.StationId ?? -1, name, v.Value);
            await Task.Delay(100, ct);
        }
    }
}
