using System.Net;
using AgentPing.Bridge.Security;
using Microsoft.AspNetCore.Http;

namespace AgentPing.Bridge.Tests;

public sealed class ManagementAccessPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("192.168.1.9", false)]
    [InlineData("8.8.8.8", false)]
    public void Only_actual_loopback_remote_addresses_are_accepted(string address, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        context.Request.Headers[ManagementAccessHeader.Name] = ManagementAccessHeader.Value;
        Assert.Equal(expected, new RemoteIpManagementAccessPolicy().IsLoopback(context));
    }

    [Fact]
    public void Missing_remote_address_is_rejected() => Assert.False(new RemoteIpManagementAccessPolicy().IsLoopback(new DefaultHttpContext()));

    [Fact]
    public void Loopback_request_without_the_non_simple_management_header_is_rejected()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;

        Assert.False(new RemoteIpManagementAccessPolicy().IsLoopback(context));
    }

    [Theory]
    [InlineData("192.168.1.9", true, true)]
    [InlineData("10.0.0.9", true, true)]
    [InlineData("172.16.0.9", true, true)]
    [InlineData("127.0.0.1", true, false)]
    [InlineData("169.254.1.9", true, false)]
    [InlineData("8.8.8.8", true, false)]
    [InlineData("192.168.1.9", false, false)]
    public void Enrollment_requires_https_on_a_private_non_loopback_interface(
        string localAddress,
        bool isHttps,
        bool expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalIpAddress = IPAddress.Parse(localAddress);
        context.Request.Scheme = isHttps ? "https" : "http";

        Assert.Equal(expected, new PrivateHttpsEnrollmentAccessPolicy().IsAllowed(context));
    }

    [Fact]
    public void Enrollment_rejects_a_missing_local_address() =>
        Assert.False(new PrivateHttpsEnrollmentAccessPolicy().IsAllowed(new DefaultHttpContext()));
}
