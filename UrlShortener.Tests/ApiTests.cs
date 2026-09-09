using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace UrlShortener.Tests;

public sealed class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        var client = factory.WithWebHostBuilder(builder =>
            builder.UseSetting(
                "ConnectionStrings:Default",
                $"Data Source=url_shortener_test_{Guid.NewGuid():N}.db")).CreateClient(
                    new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _client = client;
    }

    [Fact]
    public async Task CreateRedirectAndAnalytics()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/urls", new { url = "https://example.com/docs" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreateResponse>();
        Assert.NotNull(created);

        var redirect = await _client.GetAsync($"/{created.ShortCode}");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("https://example.com/docs", redirect.Headers.Location?.ToString());

        var analytics = await _client.GetFromJsonAsync<AnalyticsResponse>(
            $"/api/v1/urls/{created.ShortCode}/analytics");
        Assert.NotNull(analytics);
        Assert.Equal(1, analytics.ClickCount);
    }

    [Fact]
    public async Task RejectsInvalidUrl()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/urls", new { url = "not-a-url" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsNotFoundForUnknownCode()
    {
        var response = await _client.GetAsync("/doesnotexist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record CreateResponse(string ShortCode);
    private sealed record AnalyticsResponse(long ClickCount);
}
