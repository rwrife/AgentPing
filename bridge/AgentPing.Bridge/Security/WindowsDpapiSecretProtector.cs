using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AgentPing.Bridge.Security;

public sealed class WindowsDpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "AgentPing.Bridge.DeviceCredentials.v1"u8.ToArray();
    public byte[] Protect(ReadOnlySpan<byte> value) => Transform(value, true);
    public byte[] Unprotect(ReadOnlySpan<byte> value) => Transform(value, false);

    private static byte[] Transform(ReadOnlySpan<byte> value, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Device enrollment requires Windows current-user DPAPI.");
        var input = Blob.From(value); var entropy = Blob.From(Entropy); Blob output = default;
        try
        {
            var ok = protect ? CryptProtectData(ref input, null, ref entropy, 0, 0, 1, ref output) : CryptUnprotectData(ref input, 0, ref entropy, 0, 0, 1, ref output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { input.Free(); entropy.Free(); if (output.Data != 0) LocalFree(output.Data); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Length; public nint Data;
        public static Blob From(ReadOnlySpan<byte> value) { var blob = new Blob { Length = value.Length, Data = Marshal.AllocHGlobal(value.Length) }; Marshal.Copy(value.ToArray(), 0, blob.Data, value.Length); return blob; }
        public void Free() { if (Data != 0) { Marshal.FreeHGlobal(Data); Data = 0; } }
    }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptProtectData(ref Blob input, string? description, ref Blob entropy, nint reserved, nint prompt, int flags, ref Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob input, nint description, ref Blob entropy, nint reserved, nint prompt, int flags, ref Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
