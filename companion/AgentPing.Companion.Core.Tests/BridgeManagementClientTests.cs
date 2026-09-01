using System.Net;
using System.Text;
using AgentPing.Companion.Core;

namespace AgentPing.Companion.Core.Tests;

public sealed class BridgeManagementClientTests
{
    [Fact]
    public async Task Status_is_populated_from_loopback_bridge_contract()
    {
        var handler = new StubHandler("""
            {"bridge":"running","adapters":[{"name":"codex","displayName":"Codex CLI","enabled":true,"integration":"hook","capabilities":"events"}],"recentAttentions":[{"attentionId":"a1","title":"Approve?","category":"approval","responseDeadlineAt":"2026-09-01T01:00:00Z"}],"devices":[{"deviceId":"desk-1","revoked":false,"updatedUtc":"2026-09-01T00:00:00Z"}],"pairing":{"open":false,"expiresUtc":null,"attemptsRemaining":0}}
            """);
        var client = new BridgeManagementClient(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8742/") });

        var status = await client.GetStatusAsync();

        Assert.Equal("desk-1", Assert.Single(status.Devices).DeviceId);
        Assert.Equal("Codex CLI", Assert.Single(status.Adapters).DisplayName);
        Assert.Equal("Approve?", Assert.Single(status.RecentAttentions).Title);
        Assert.Equal("http://127.0.0.1:8742/management/status", handler.LastRequest!.RequestUri!.AbsoluteUri);
        Assert.Equal(ManagementAccessHeader.Value, Assert.Single(handler.LastRequest.Headers.GetValues(ManagementAccessHeader.Name)));
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
