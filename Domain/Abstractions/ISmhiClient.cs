using System.Threading;
using System.Threading.Tasks;
using weather_report_smhi.Domain.Models;

namespace weather_report_smhi.Domain.Abstractions;

public interface ISmhiClient
{
    Task<StationSetDataResponse?> GetLatestHourTemperatureAllAsync(CancellationToken ct);
    Task<StationSetResponse?> GetStationsAsync(int parameterId, CancellationToken ct);
    Task<StationSetDataResponse?> GetLatestMonthsForStationAsync(int parameterId, int stationId, CancellationToken ct);
    Task<StationSetDataResponse?> GetLatestDayTemperatureAllAsync(CancellationToken ct);
    Task<StationSetDataResponse?> GetLatestDayTemperatureForStationAsync(int stationId, CancellationToken ct);

}
