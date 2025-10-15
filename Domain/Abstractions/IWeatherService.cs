using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace weather_report_smhi.Domain.Abstractions;

public interface IWeatherService
{
    Task<double?> GetSwedenAverageTemperatureLatestHourAsync(CancellationToken ct);
    Task<(double totalMm, IReadOnlyList<string> months)> GetLundTotalRainLatestMonthsAsync(CancellationToken ct);
    IAsyncEnumerable<(int stationId, string stationName, double? tempC)> StreamAllStationsTemperatureAsync(CancellationToken ct);
}
