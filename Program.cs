using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using weather_report_smhi.Application;
using weather_report_smhi.Domain.Abstractions;
using weather_report_smhi.Domain.Constants;
using weather_report_smhi.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient<ISmhiClient, SmhiClient>(c =>
{
    c.BaseAddress = new Uri(MetObs.BaseUrl);
    c.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddScoped<IWeatherService, WeatherService>();

var app = builder.Build();

using var scope = app.Services.CreateScope();
var svc = scope.ServiceProvider.GetRequiredService<IWeatherService>();

using var cts = new CancellationTokenSource();

try
{
    // 1) Average temperature in Sweden (latest hour)
    var avg = await svc.GetSwedenAverageTemperatureLatestHourAsync(cts.Token);
    Console.WriteLine(avg is null
        ? "No temperature data available for the latest hour."
        : $"Average temperature in Sweden (latest hour): {avg:F1} °C");

    // 2) Total rainfall in Lund (latest months)
    var (totalMm, months) = await svc.GetLundTotalRainLatestMonthsAsync(cts.Token);
    Console.WriteLine(months.Count > 0
        ? $"Total rainfall in Lund for latest months [{string.Join(", ", months)}]: {totalMm:F1} mm"
        : "No Lund rainfall data found.");

    // 3) Streaming with cancellation
    Console.WriteLine("\nStreaming station temperatures (press any key to cancel)...");
    using var keyCts = new CancellationTokenSource();
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, keyCts.Token);

    // Fire-and-forget key listener (keeps main loop clean/readable)
    _ = Task.Run(() => { Console.ReadKey(true); keyCts.Cancel(); });

    await foreach (var (id, name, t) in svc.StreamAllStationsTemperatureAsync(linked.Token))
    {
        var val = t.HasValue ? $"{t.Value:F1} °C" : "n/a";
        Console.WriteLine($"[{id}] {name}: {val}");
    }

    Console.WriteLine("Stopped.");
}
catch (OperationCanceledException)
{
    Console.WriteLine("Cancelled by user.");
}
catch (Exception ex)
{
    // Review-friendly: fail visibly with a single, clear log line.
    Console.Error.WriteLine($"Unexpected error: {ex.Message}");
    Environment.ExitCode = 1;
}
