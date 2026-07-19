using System.Security.Cryptography;
using System.Text;

namespace RPGGame.Server.Services;

/// <summary>
/// Manages reconnect tokens. Tokens are HMAC-SHA256(playerName + expiry + secret).
/// </summary>
public static class SessionManager
{
    private static readonly Dictionary<string, (string PlayerName, long Expiry)> _tokens = new();
    private static readonly string _secret = Environment.GetEnvironmentVariable("SESSION_SECRET")
        ?? "dev-secret-change-in-production";

    /// <summary>
    /// Creates a new reconnect token for a player (valid 7 days).
    /// </summary>
    public static string CreateToken(string playerName)
    {
        var expiry = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();
        var payload = $"{playerName}:{expiry}";
        var token = ComputeHmac(payload);
        var fullToken = $"{payload}:{token}";

        lock (_tokens)
        {
            _tokens[fullToken] = (playerName, expiry);
        }

        return fullToken;
    }

    /// <summary>
    /// Validates token and removes it (one-time use).
    /// Returns playerName if valid, null otherwise.
    /// </summary>
    public static string? ValidateAndConsume(string token)
    {
        lock (_tokens)
        {
            if (!_tokens.TryGetValue(token, out var info))
                return null;

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > info.Expiry)
            {
                _tokens.Remove(token);
                return null;
            }

            // Verify HMAC
            var parts = token.Split(':');
            if (parts.Length != 3) return null;

            var payload = $"{parts[0]}:{parts[1]}";
            var providedHmac = parts[2];
            var expectedHmac = ComputeHmac(payload);

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(providedHmac),
                    Encoding.UTF8.GetBytes(expectedHmac)))
            {
                return null;
            }

            _tokens.Remove(token);
            return info.PlayerName;
        }
    }

    /// <summary>
    /// Cleans up expired tokens. Call periodically.
    /// </summary>
    public static void Cleanup()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_tokens)
        {
            var expired = _tokens.Where(kvp => kvp.Value.Expiry < now).Select(kvp => kvp.Key).ToList();
            foreach (var key in expired) _tokens.Remove(key);
        }
    }

    private static string ComputeHmac(string payload)
    {
        var key = Encoding.UTF8.GetBytes(_secret);
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
}