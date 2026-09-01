using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using AgentPing.Bridge.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPing.Bridge.Tests;

public sealed class EnrollmentManagementEndpointTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"agentping-enrollment-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;
    public EnrollmentManagementEndpointTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Bridge:PersistencePath", Path.Combine(_directory, "state.json"));
            builder.UseSetting("Bridge:DeviceCredentialsPath", Path.Combine(_directory, "credentials.json"));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISecretProtector>();
                services.AddSingleton<ISecretProtector, TestProtector>();
                services.RemoveAll<IManagementAccessPolicy>();
                services.AddSingleton<IManagementAccessPolicy, TestLoopbackPolicy>();
                services.RemoveAll<IEnrollmentAccessPolicy>();
                services.AddSingleton<IEnrollmentAccessPolicy, TestEnrollmentPolicy>();
            });
        });
    }

    [Fact]
    public async Task Enrollment_requires_https_is_single_use_and_token_is_not_plaintext()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost") });
        var opened = await OpenAsync(client);
        using var insecure = await client.PostAsJsonAsync("/enroll", new EnrollmentRequest(opened.EnrollmentSecret, "display-1"));
        Assert.Equal(HttpStatusCode.UpgradeRequired, insecure.StatusCode);

        var enrolled = await client.PostAsJsonAsync("https://localhost/enroll", new EnrollmentRequest(opened.EnrollmentSecret, "display-1"));
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        var token = (await enrolled.Content.ReadFromJsonAsync<DeviceTokenResponse>())!.Token;
        Assert.Equal(32, Decode(token).Length);
        Assert.DoesNotContain(token, await File.ReadAllTextAsync(Path.Combine(_directory, "credentials.json")), StringComparison.Ordinal);
        using var replay = await client.PostAsJsonAsync("https://localhost/enroll", new EnrollmentRequest(opened.EnrollmentSecret, "display-2"));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Five_bad_attempts_exhaust_window_and_rotate_revoke_change_authentication()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var opened = await OpenAsync(client);
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/enroll", new EnrollmentRequest(Base64Url.Encode(RandomNumberGenerator.GetBytes(32)), "bad"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/enroll", new EnrollmentRequest(opened.EnrollmentSecret, "display-1"))).StatusCode);

        opened = await OpenAsync(client);
        var issued = (await (await client.PostAsJsonAsync("/enroll", new EnrollmentRequest(opened.EnrollmentSecret, "display-1"))).Content.ReadFromJsonAsync<DeviceTokenResponse>())!;
        var manager = _factory.Services.GetRequiredService<DeviceCredentialManager>();
        Assert.NotNull(await manager.AuthenticateAsync(issued.Token));
        var rotated = (await (await client.PostAsync("/management/devices/display-1/rotate", null)).Content.ReadFromJsonAsync<DeviceTokenResponse>())!;
        Assert.Null(await manager.AuthenticateAsync(issued.Token));
        Assert.NotNull(await manager.AuthenticateAsync(rotated.Token));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/management/devices/display-1")).StatusCode);
        Assert.Null(await manager.AuthenticateAsync(rotated.Token));
    }

    [Fact]
    public async Task Pairing_window_rejects_missing_certificate_fingerprint_without_a_server_error()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var response = await client.PostAsJsonAsync("/management/pairing-window", new
        {
            tlsEndpoint = "https://192.168.1.10:8743/enroll",
            lifetimeSeconds = 300,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public async Task Pairing_window_rejects_lifetimes_outside_one_to_three_hundred_seconds(int lifetimeSeconds)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var response = await client.PostAsJsonAsync("/management/pairing-window", new OpenPairingWindowRequest(
            "https://192.168.1.10:8743/enroll",
            new string('a', 64),
            lifetimeSeconds));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Device_management_rejects_an_invalid_device_identifier_without_a_server_error()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var response = await client.PostAsync("/management/devices/bad!/rotate", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<PairingProvisioningBundle> OpenAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/management/pairing-window", new OpenPairingWindowRequest("https://192.168.1.10:8743/enroll", new string('a', 64)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PairingProvisioningBundle>())!;
    }
    private static byte[] Decode(string token) { Base64Url.TryDecode(token, out var bytes); return bytes; }
    public void Dispose() { _factory.Dispose(); if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    private sealed class TestProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> value) => value.ToArray().Select(x => (byte)(x ^ 0x5a)).ToArray();
        public byte[] Unprotect(ReadOnlySpan<byte> value) => Protect(value);
    }
    private sealed class TestLoopbackPolicy : IManagementAccessPolicy { public bool IsLoopback(HttpContext context) => true; }
    private sealed class TestEnrollmentPolicy : IEnrollmentAccessPolicy { public bool IsAllowed(HttpContext context) => context.Request.IsHttps; }
}
