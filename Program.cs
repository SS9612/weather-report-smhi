using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using weather_report_smhi.Application;
using weather_report_smhi.Domain.Abstractions;
using weather_report_smhi.Domain.Constants;
using weather_report_smhi.Infrastructure;
using System.Net;
using System.Net.Http;

var builder = Host.CreateApplicationBuilder(args);

// 1) Turn down built-in HttpClient INFO spam
builder.Logging.SetMinimumLevel(LogLevel.Information); 
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.ISmhiClient", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.ISmhiClient.ClientHandler", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.ISmhiClient.LogicalHandler", LogLevel.Warning);

builder.Services.AddTransient<QuietHttpLoggingHandler>();

builder.Services.AddHttpClient<ISmhiClient, SmhiClient>(c =>
{
    c.BaseAddress = new Uri(MetObs.BaseUrl);
    c.Timeout = TimeSpan.FromSeconds(15);
})

.AddHttpMessageHandler<QuietHttpLoggingHandler>()

.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    MaxConnectionsPerServer = 8
});

builder.Services.AddScoped<IWeatherService, WeatherService>();

var app = builder.Build();

using var scope = app.Services.CreateScope();
var svc = scope.ServiceProvider.GetRequiredService<IWeatherService>();

using var cts = new CancellationTokenSource();

try
{
    while (!cts.Token.IsCancellationRequested)
    {
        ShowMenu();
        var key = Console.ReadKey(true);
        Console.WriteLine();

        try
        {
            switch (key.KeyChar)
            {
                case '1':
                    await DisplayAverageTemperatureAsync(svc, cts.Token);
                    break;
                case '2':
                    await DisplayLundRainfallAsync(svc, cts.Token);
                    break;
                case '3':
                    await DisplayAllStationTemperaturesAsync(svc, cts.Token);
                    break;
                case '4':
                    Console.WriteLine("Data will be refreshed on next selection.\n");
                    break;
                case 'q':
                case 'Q':
                    cts.Cancel();
                    Console.WriteLine("Exiting...");
                    return;
                default:
                    Console.WriteLine("Invalid option. Please select 1-4 or 'q' to quit.\n");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}\n");
        }

        if (!cts.Token.IsCancellationRequested)
        {
            Console.WriteLine("Press any key to return to menu...");
            Console.ReadKey(true);
            Console.Clear();
        }
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Cancelled by user.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unexpected error: {ex.Message}");
    Environment.ExitCode = 1;
}

static void ShowMenu()
{
    Console.WriteLine("=== Weather Report Menu ===");
    Console.WriteLine("(1) Display Average temperature in Sweden (latest hour)");
    Console.WriteLine("(2) Display Total rainfall in Lund (latest months)");
    Console.WriteLine("(3) Display all station temperatures (with values)");
    Console.WriteLine("(4) Refresh data");
    Console.WriteLine("(q) Quit");
    Console.Write("\nSelect an option: ");
}

static async Task DisplayAverageTemperatureAsync(IWeatherService svc, CancellationToken ct)
{
    Console.WriteLine("\nFetching average temperature data...");
    var avg = await svc.GetSwedenAverageTemperatureLatestHourAsync(ct);
    Console.WriteLine(avg is null
        ? "No temperature data available for the latest hour."
        : $"Average temperature in Sweden (latest hour): {avg:F1} °C");
}

static async Task DisplayLundRainfallAsync(IWeatherService svc, CancellationToken ct)
{
    Console.WriteLine("\nFetching Lund rainfall data...");
    var (totalMm, months) = await svc.GetLundTotalRainLatestMonthsAsync(ct);
    Console.WriteLine(months.Count > 0
        ? $"Total rainfall in Lund for latest months [{string.Join(", ", months)}]: {totalMm:F1} mm"
        : "No Lund rainfall data found.");
}

static async Task DisplayAllStationTemperaturesAsync(IWeatherService svc, CancellationToken ct)
{
    Console.WriteLine("\nFetching all station temperatures...\n");
    var count = 0;
    await foreach (var (id, name, t) in svc.StreamAllStationsTemperatureAsync(ct))
    {
        var val = t.HasValue ? $"{t.Value:F1} °C" : "n/a";
        Console.WriteLine($"[{id}] {name}: {val}");
        count++;
    }
    Console.WriteLine($"\nTotal stations displayed: {count}");
}

internal sealed class QuietHttpLoggingHandler : DelegatingHandler
{
    private readonly ILogger<QuietHttpLoggingHandler> _logger;

    public QuietHttpLoggingHandler(ILogger<QuietHttpLoggingHandler> logger)
        => _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);

        // Suppress 404 noise completely
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return response;

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("HTTP {Status} {Method} {Url}",
                (int)response.StatusCode, request.Method, request.RequestUri);
        }

        return response;
    }
}
