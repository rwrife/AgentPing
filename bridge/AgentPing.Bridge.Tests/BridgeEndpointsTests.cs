using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AgentPing.Bridge.Tests;

public sealed class BridgeEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public BridgeEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        using var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Status_endpoint_returns_the_baseline_contract()
    {
        var response = await _client.GetFromJsonAsync<BridgeStatus>("/api/status");

        Assert.NotNull(response);
        Assert.Equal("agentping-bridge", response.Service);
        Assert.Equal("ok", response.Status);
        Assert.Equal("baseline-v0", response.ApiVersion);
        Assert.InRange(
            response.TimestampUtc,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(1));
    }
}
