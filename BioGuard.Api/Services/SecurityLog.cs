using System.Security.Cryptography;
using System.Text;

namespace BioGuard.Api.Services;

public static class SecurityLog
{
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "empty";
        var parts = email.Trim().Split('@', 2);
        if (parts.Length != 2) return Fingerprint(email);

        var local = parts[0];
        var visible = local.Length <= 1 ? local : local[..1];
        return $"{visible}***@{parts[1].ToLowerInvariant()}";
    }

    public static string Fingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "empty";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
