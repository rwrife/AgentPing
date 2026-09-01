namespace AgentPing.Bridge.Security;

public static class ManagementAccessHeader
{
    public const string Name = "X-AgentPing-Management";
    public const string Value = "companion-v1";
}

public interface IManagementAccessPolicy { bool IsLoopback(HttpContext context); }

public sealed class RemoteIpManagementAccessPolicy : IManagementAccessPolicy
{
    public bool IsLoopback(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address
        && System.Net.IPAddress.IsLoopback(address)
        && context.Request.Headers.TryGetValue(ManagementAccessHeader.Name, out var value)
        && value.Count == 1
        && value[0] == ManagementAccessHeader.Value;
}

public interface IEnrollmentAccessPolicy { bool IsAllowed(HttpContext context); }

public sealed class PrivateHttpsEnrollmentAccessPolicy : IEnrollmentAccessPolicy
{
    public bool IsAllowed(HttpContext context)
    {
        if (!context.Request.IsHttps
            || context.Connection.LocalIpAddress is not { } address
            || System.Net.IPAddress.IsLoopback(address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
            || bytes[0] == 192 && bytes[1] == 168;
    }
}
