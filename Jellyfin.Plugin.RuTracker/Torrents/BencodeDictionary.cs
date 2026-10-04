using System;
using System.Collections.Generic;
using System.Text;

namespace Jellyfin.Plugin.RuTracker.Torrents;

/// <summary>
/// A bencode dictionary that also remembers where each value sits in the source bytes
/// (needed to hash the exact <c>info</c> dictionary).
/// </summary>
internal sealed class BencodeDictionary
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Start, int End)> _ranges = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the offset of this dictionary in the source.
    /// </summary>
    public int RawStart { get; set; }

    /// <summary>
    /// Gets or sets the end offset (exclusive) of this dictionary in the source.
    /// </summary>
    public int RawEnd { get; set; }

    /// <summary>
    /// Adds a value.
    /// </summary>
    /// <param name="key">Key.</param>
    /// <param name="value">Value.</param>
    /// <param name="start">Value start offset.</param>
    /// <param name="end">Value end offset (exclusive).</param>
    public void Add(string key, object value, int start, int end)
    {
        _values[key] = value;
        _ranges[key] = (start, end);
    }

    /// <summary>
    /// Gets the byte range of a value in the source.
    /// </summary>
    /// <param name="key">Key.</param>
    /// <param name="range">Start and end offsets.</param>
    /// <returns><c>true</c> if the key exists.</returns>
    public bool TryGetRange(string key, out (int Start, int End) range) => _ranges.TryGetValue(key, out range);

    /// <summary>
    /// Gets a nested dictionary.
    /// </summary>
    /// <param name="key">Key.</param>
    /// <returns>The dictionary, or <c>null</c>.</returns>
    public BencodeDictionary? GetDictionary(string key) => _values.GetValueOrDefault(key) as BencodeDictionary;

    /// <summary>
    /// Gets a list.
    /// </summary>
    /// <param name="key">Key.</param>
    /// <returns>The list, or <c>null</c>.</returns>
    public List<object>? GetList(string key) => _values.GetValueOrDefault(key) as List<object>;

    /// <summary>
    /// Gets an integer.
    /// </summary>
    /// <param name="key">Key.</param>
    /// <returns>The value, or <c>null</c>.</returns>
    public long? GetInteger(string key) => _values.GetValueOrDefault(key) as long?;

    /// <summary>
    /// Gets a string decoded as UTF-8.
    /// </summary>
    /// <param name="key">Key.</param>
    /// <returns>The value, or <c>null</c>.</returns>
    public string? GetString(string key)
        => _values.GetValueOrDefault(key) is byte[] bytes ? Encoding.UTF8.GetString(bytes) : null;
}
