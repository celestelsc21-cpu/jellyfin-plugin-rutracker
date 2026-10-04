using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.RuTracker.Torrents;

/// <summary>
/// Minimal, defensive bencode reader for .torrent files.
/// Values: <see cref="long"/>, <see cref="byte"/>[] (strings), <see cref="List{T}"/> of object,
/// and <see cref="BencodeDictionary"/>.
/// </summary>
internal static class Bencode
{
    private const int MaxDepth = 64;

    /// <summary>
    /// Parses a bencoded document.
    /// </summary>
    /// <param name="data">Raw bytes.</param>
    /// <returns>Root value.</returns>
    /// <exception cref="FormatException">The data is not valid bencode.</exception>
    public static object Parse(ReadOnlySpan<byte> data)
    {
        var position = 0;
        var value = ReadValue(data, ref position, 0);
        if (position != data.Length)
        {
            throw new FormatException("Trailing data after bencoded value.");
        }

        return value;
    }

    private static object ReadValue(ReadOnlySpan<byte> data, ref int position, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new FormatException("Bencode nesting is too deep.");
        }

        if (position >= data.Length)
        {
            throw new FormatException("Unexpected end of bencode data.");
        }

        switch (data[position])
        {
            case (byte)'i':
                return ReadInteger(data, ref position);
            case (byte)'l':
                {
                    position++;
                    var list = new List<object>();
                    while (Peek(data, position) != (byte)'e')
                    {
                        list.Add(ReadValue(data, ref position, depth + 1));
                    }

                    position++;
                    return list;
                }

            case (byte)'d':
                {
                    var start = position;
                    position++;
                    var dict = new BencodeDictionary();
                    while (Peek(data, position) != (byte)'e')
                    {
                        var key = Encoding.UTF8.GetString(ReadBytes(data, ref position));
                        var valueStart = position;
                        var value = ReadValue(data, ref position, depth + 1);
                        dict.Add(key, value, valueStart, position);
                    }

                    position++;
                    dict.RawStart = start;
                    dict.RawEnd = position;
                    return dict;
                }

            default:
                return ReadBytes(data, ref position);
        }
    }

    private static byte Peek(ReadOnlySpan<byte> data, int position)
        => position < data.Length ? data[position] : throw new FormatException("Unexpected end of bencode data.");

    private static long ReadInteger(ReadOnlySpan<byte> data, ref int position)
    {
        var end = data[position..].IndexOf((byte)'e');
        if (end < 0)
        {
            throw new FormatException("Unterminated bencode integer.");
        }

        var text = Encoding.ASCII.GetString(data.Slice(position + 1, end - 1));
        position += end + 1;
        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException("Invalid bencode integer.");
    }

    private static byte[] ReadBytes(ReadOnlySpan<byte> data, ref int position)
    {
        var colon = data[position..].IndexOf((byte)':');
        if (colon <= 0 || colon > 10)
        {
            throw new FormatException("Invalid bencode string length.");
        }

        var lengthText = Encoding.ASCII.GetString(data.Slice(position, colon));
        if (!int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out var length)
            || position + colon + 1 + length > data.Length)
        {
            throw new FormatException("Invalid bencode string length.");
        }

        var bytes = data.Slice(position + colon + 1, length).ToArray();
        position += colon + 1 + length;
        return bytes;
    }
}
