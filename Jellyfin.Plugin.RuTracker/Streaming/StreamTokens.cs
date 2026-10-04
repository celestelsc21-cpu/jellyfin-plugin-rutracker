using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.RuTracker.Streaming;

/// <summary>
/// Signs stream URLs so the anonymous stream endpoint only serves links issued by the plugin.
/// The key lives in memory: links stop working after a server restart, and Jellyfin
/// re-requests media info anyway.
/// </summary>
public sealed class StreamTokens
{
    private readonly byte[] _key;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamTokens"/> class with a random key.
    /// </summary>
    public StreamTokens()
        : this(RandomNumberGenerator.GetBytes(32), TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamTokens"/> class.
    /// </summary>
    /// <param name="key">HMAC key.</param>
    /// <param name="time">Clock.</param>
    internal StreamTokens(byte[] key, TimeProvider time)
    {
        _key = key;
        _time = time;
    }

    /// <summary>
    /// Gets how long issued links stay valid.
    /// </summary>
    public static TimeSpan Lifetime { get; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Creates a signature for a file of a download.
    /// </summary>
    /// <param name="downloadId">Download id.</param>
    /// <param name="fileIndex">File index.</param>
    /// <param name="expires">Unix time (seconds) when the link expires.</param>
    /// <returns>URL-safe signature.</returns>
    public string Sign(Guid downloadId, int fileIndex, long expires)
    {
        var payload = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{downloadId:N}|{fileIndex}|{expires}"));
        return Convert.ToBase64String(HMACSHA256.HashData(_key, payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Creates a query string with expiry and signature.
    /// </summary>
    /// <param name="downloadId">Download id.</param>
    /// <param name="fileIndex">File index.</param>
    /// <returns>Query string without the leading "?".</returns>
    public string Query(Guid downloadId, int fileIndex)
    {
        var expires = (_time.GetUtcNow() + Lifetime).ToUnixTimeSeconds();
        return "exp=" + expires.ToString(CultureInfo.InvariantCulture) + "&sig=" + Sign(downloadId, fileIndex, expires);
    }

    /// <summary>
    /// Validates a signature in constant time.
    /// </summary>
    /// <param name="downloadId">Download id.</param>
    /// <param name="fileIndex">File index.</param>
    /// <param name="expires">Expiry from the URL.</param>
    /// <param name="signature">Signature from the URL.</param>
    /// <returns><c>true</c> when valid and not expired.</returns>
    public bool IsValid(Guid downloadId, int fileIndex, long expires, string? signature)
    {
        if (string.IsNullOrEmpty(signature) || expires < _time.GetUtcNow().ToUnixTimeSeconds())
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(downloadId, fileIndex, expires));
        var actual = Encoding.ASCII.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
