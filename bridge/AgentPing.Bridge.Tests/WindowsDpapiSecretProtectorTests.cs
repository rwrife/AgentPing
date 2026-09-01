using System.Security.Cryptography;
using AgentPing.Bridge.Security;

namespace AgentPing.Bridge.Tests;

public sealed class WindowsDpapiSecretProtectorTests
{
    [Fact]
    public void Current_user_dpapi_round_trips_secret_material_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var plaintext = RandomNumberGenerator.GetBytes(32);
        var protector = new WindowsDpapiSecretProtector();
        var protectedValue = protector.Protect(plaintext);
        var roundTripped = protector.Unprotect(protectedValue);
        try
        {
            Assert.NotEqual(plaintext, protectedValue);
            Assert.Equal(plaintext, roundTripped);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(protectedValue);
            CryptographicOperations.ZeroMemory(roundTripped);
        }
    }
}