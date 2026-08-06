using System.Security.Cryptography;
using System.Text;
using LostAndDivine.Server.Repositories;

namespace LostAndDivine.Server.Services;

/// <summary>
/// Manages reconnect tokens. Tokens are HMAC-SHA256(playerName + expiry + secret)
/// and are persisted in SQLite so they survive a server restart.
/// </summary>
public static class SessionManager
{
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

        // A player only needs one live token at a time.
        SessionTokenRepository.DeleteForPlayer(playerName);
        SessionTokenRepository.Save(fullToken, playerName, expiry);

        return fullToken;
    }

    /// <summary>
    /// Validates token and removes it (one-time use).
    /// Returns playerName if valid, null otherwise.
    /// </summary>
    public static string? ValidateAndConsume(string token)
    {
        if (TryValidate(token, out var playerName))
        {
            SessionTokenRepository.Delete(token);
            return playerName;
        }

        return null;
    }

    /// <summary>
    /// Validates a token WITHOUT consuming it. Used for retryable reconnect
    /// attempts, where a failed attempt must not burn the token.
    /// Returns playerName if valid, null otherwise.
    /// </summary>
    public static string? Validate(string token)
        => TryValidate(token, out var playerName) ? playerName : null;

    /// <summary>
    /// Removes a token from the store. Call after a successful reconnect
    /// (the session gets a fresh token instead).
    /// </summary>
    public static void Revoke(string token)
        => SessionTokenRepository.Delete(token);

    /// <summary>
    /// Cleans up expired tokens. Call periodically.
    /// </summary>
    public static void Cleanup()
        => SessionTokenRepository.DeleteExpired(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private static bool TryValidate(string token, out string playerName)
    {
        playerName = string.Empty;

        if (string.IsNullOrEmpty(token))
            return false;

        var info = SessionTokenRepository.Find(token);
        if (info == null)
            return false;

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > info.Value.Expiry)
        {
            SessionTokenRepository.Delete(token);
            return false;
        }

        // Verify HMAC
        var parts = token.Split(':');
        if (parts.Length != 3) return false;

        var payload = $"{parts[0]}:{parts[1]}";
        var expectedHmac = ComputeHmac(payload);

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(parts[2]),
                Encoding.UTF8.GetBytes(expectedHmac)))
        {
            return false;
        }

        playerName = info.Value.PlayerName;
        return true;
    }

    private static string ComputeHmac(string payload)
    {
        var key = Encoding.UTF8.GetBytes(_secret);
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
}
