namespace weather_report_smhi.Domain.Constants;

public static class MetObs
{
    public const string BaseUrl = "https://opendata-download-metobs.smhi.se";
    public const int TemperatureParam = 1;       // Lufttemperatur (°C)
    public const int MonthlyPrecipParam = 23;    // Nederbörd, summa 1 månad (mm)
}
