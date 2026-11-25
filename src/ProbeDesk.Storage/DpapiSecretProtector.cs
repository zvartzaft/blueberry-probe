using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Storage;

/// <summary>DPAPI (current user). Windows only in v1; see docs/decisions/0004-storage.md.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "ProbeDesk.v1"u8.ToArray();

    public DpapiSecretProtector()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Secret storage currently requires Windows (DPAPI).");
    }

    public string Protect(string plain) => Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));

    public string Unprotect(string protectedValue) => Encoding.UTF8.GetString(
        ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser));
}
