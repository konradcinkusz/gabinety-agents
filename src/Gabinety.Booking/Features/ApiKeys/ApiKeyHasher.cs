using System.Security.Cryptography;
using System.Text;

namespace Gabinety.Booking.Features.ApiKeys;

/// <summary>Creates and hashes API keys (docs/agentic/PLAN.md 2.2): <c>gab_</c> followed by 40 base62 characters from the system CSPRNG.</summary>
public static class ApiKeyHasher
{
    public const string KeyPrefix = "gab_";
    public const int RandomLength = 40;
    public const int PrefixLength = 8;
    const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>A new plain key. The caller shows it once and stores only <see cref="Hash"/> and <see cref="PrefixOf"/>.</summary>
    public static string Generate() => KeyPrefix + string.Create(RandomLength, 0, static (span, _) => RandomNumberGenerator.GetItems<char>(Base62, span));

    /// <summary>SHA-256 of the UTF-8 key as 64 lowercase hex characters.</summary>
    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    /// <summary>The 8 characters after <c>gab_</c>.</summary>
    public static string PrefixOf(string key) => key.Substring(KeyPrefix.Length, PrefixLength);
}
