using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using weather_report_smhi.Application;
using weather_report_smhi.Domain.Abstractions;
using weather_report_smhi.Domain.Constants;
using weather_report_smhi.Infrastructure;
using System.Net;
using System.Net.Http;

// -----------------------------
// Program
// -----------------------------
var builder = Host.CreateApplicationBuilder(args);

// 1) Turn down built-in HttpClient INFO spam
builder.Logging.SetMinimumLevel(LogLevel.Information); // app defaults
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.ISmhiClient", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.ISmhiClient.ClientHandler", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.ISmhiClient.LogicalHandler", LogLevel.Warning);

// 2) Register a quiet handler that only logs non-success HTTP results (once)
builder.Services.AddTransient<QuietHttpLoggingHandler>();

// 3) Typed client with polite connection limits
builder.Services.AddHttpClient<ISmhiClient, SmhiClient>(c =>
{
    c.BaseAddress = new Uri(MetObs.BaseUrl);
    c.Timeout = TimeSpan.FromSeconds(15);
})
// log only failures, not every start/end
.AddHttpMessageHandler<QuietHttpLoggingHandler>()
// be polite & stable under load
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
    Console.Error.WriteLine($"Unexpected error: {ex.Message}");
    Environment.ExitCode = 1;
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
