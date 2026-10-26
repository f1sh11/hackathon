using System.Text.Json.Nodes;

namespace SydneyBuddy.Api.Tests;

/// <summary>验证逐日天气与未知状态；weather failures must never become invented forecasts.</summary>
public sealed class WeatherTests
{
    internal static JsonObject Forecast(int day, double probability = 10) => new()
    {
        ["displayDate"] = new JsonObject { ["year"] = 2026, ["month"] = 10, ["day"] = day },
        ["minTemperature"] = new JsonObject { ["degrees"] = 15 },
        ["maxTemperature"] = new JsonObject { ["degrees"] = 24 },
        ["daytimeForecast"] = Part(probability)
    };

    private static JsonObject Part(double probability, string type = "CLOUDY") => new()
    {
        ["weatherCondition"] = new JsonObject
        {
            ["description"] = new JsonObject { ["text"] = "Cloudy" }, ["type"] = type
        },
        ["precipitation"] = new JsonObject { ["probability"] = new JsonObject { ["percent"] = probability } }
    };

    [Fact]
    public async Task DailyForecastsUseHeaderCredentialAndTenDayPagination()
    {
        var external = new FakeExternal { Respond = _ => new JsonObject { ["forecastDays"] = new JsonArray(Forecast(24, 80), Forecast(25, 10)) } };
        var actual = await new WeatherService(external).GetAsync(TestSupport.Profile(2), "test-weather-credential");
        Assert.Equal("ok", actual.Str("status"));
        Assert.True(actual.Array("days")![0]!.Bool("rainy"));
        Assert.False(actual.Array("days")![1]!.Bool("rainy"));
        var call = Assert.Single(external.Calls);
        Assert.Contains("days=10", call.Url);
        Assert.Contains("pageSize=10", call.Url);
        Assert.Contains("languageCode=en&", call.Url);
        Assert.Contains("chance of rain", actual.Str("summary"));
        Assert.DoesNotContain("test-weather-credential", call.Url);
        Assert.Equal("test-weather-credential", call.Headers["X-Goog-Api-Key"]);
        Assert.Equal(8, call.TimeoutSeconds);
    }

    [Fact]
    public async Task MissingRequestedDateIsExplicitlyOutOfRange()
    {
        var external = new FakeExternal { Respond = _ => new JsonObject { ["forecastDays"] = new JsonArray(Forecast(24)) } };
        var profile = TestSupport.Profile(); profile["date"] = "2027-01-01";
        var actual = await new WeatherService(external).GetAsync(profile, "test-key");
        Assert.Equal("out_of_range", actual.Str("status"));
        Assert.False(Assert.Single(actual.Array("days")!)!.Bool("available"));
    }

    [Fact]
    public async Task PartiallyAvailableForecastRetainsAvailableDay()
    {
        var external = new FakeExternal { Respond = _ => new JsonObject { ["forecastDays"] = new JsonArray(Forecast(24, 70)) } };
        var actual = await new WeatherService(external).GetAsync(TestSupport.Profile(2), "test-key");
        Assert.Equal("partial", actual.Str("status"));
        Assert.True(actual.Array("days")![0]!.Bool("available"));
        Assert.False(actual.Array("days")![1]!.Bool("available"));
        Assert.True(actual.Bool("rainy"));
    }

    [Fact]
    public async Task ProviderFailureNeverBecomesSunnyForecast()
    {
        var external = new FakeExternal { Respond = _ => throw new ServiceException("provider failed") };
        var actual = await new WeatherService(external).GetAsync(TestSupport.Profile(2), "test-key");
        Assert.Equal("unavailable", actual.Str("status"));
        Assert.Contains("Clear skies have not been assumed", actual.Str("summary"));
        Assert.All(actual.Array("days")!, day => Assert.False(day!.Bool("available")));
    }

    [Fact]
    public async Task MissingPrecipitationProbabilityIsUnknownRatherThanZero()
    {
        var row = Forecast(24); row.Object("daytimeForecast")!.Remove("precipitation");
        var external = new FakeExternal { Respond = _ => new JsonObject { ["forecastDays"] = new JsonArray(row.DeepClone()) } };
        var actual = await new WeatherService(external).GetAsync(TestSupport.Profile(), "test-key");
        Assert.Equal("unavailable", actual.Str("status"));
        Assert.False(actual.Array("days")![0]!.Bool("available"));
    }

    [Fact]
    public async Task EveningTravelUsesNightWeatherRatherThanDayWeather()
    {
        var row = Forecast(24, 5); row["nighttimeForecast"] = Part(90, "RAIN");
        var external = new FakeExternal { Respond = _ => new JsonObject { ["forecastDays"] = new JsonArray(row.DeepClone()) } };
        var profile = TestSupport.Profile(); profile["startMinute"] = 1200; profile["endMinute"] = 1320;
        var actual = await new WeatherService(external).GetAsync(profile, "test-key");
        Assert.True(actual.Bool("rainy"));
        Assert.Equal(90, actual.Array("days")![0]!.Number("precipitationProbability"));
    }

    [Fact]
    public async Task RequestCancellationIsPropagated()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var external = new FakeExternal();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WeatherService(external)
            .GetAsync(TestSupport.Profile(), "test-key", cancellation.Token));
    }
}
