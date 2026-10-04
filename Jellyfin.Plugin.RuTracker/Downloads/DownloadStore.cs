using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RuTracker.Downloads;

/// <summary>
/// Persists downloads as JSON next to the plugin configuration (a folder that
/// survives plugin updates). All access is serialized; writes are atomic.
/// </summary>
internal sealed class DownloadStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger<DownloadStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<DownloadRecord>? _records;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadStore"/> class.
    /// </summary>
    /// <param name="paths">Application paths.</param>
    /// <param name="logger">Logger.</param>
    public DownloadStore(IApplicationPaths paths, ILogger<DownloadStore> logger)
    {
        _path = Path.Combine(paths.PluginConfigurationsPath, "Jellyfin.Plugin.RuTracker", "downloads.json");
        _logger = logger;
    }

    /// <summary>
    /// Gets a snapshot copy of all records.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Records, newest first.</returns>
    public async Task<IReadOnlyList<DownloadRecord>> GetAllAsync(CancellationToken cancellationToken)
        => await UpdateAsync(records => records.Select(Clone).OrderByDescending(r => r.Created).ToList(), save: false, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Runs a change over the record list and saves it.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="change">Change to apply; receives the live list.</param>
    /// <param name="save">Whether to persist afterwards.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The change result.</returns>
    public async Task<T> UpdateAsync<T>(Func<List<DownloadRecord>, T> change, bool save, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _records ??= await LoadAsync(cancellationToken).ConfigureAwait(false);
            var result = change(_records);
            if (save)
            {
                await SaveAsync(_records, cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private static DownloadRecord Clone(DownloadRecord r)
    {
        var json = JsonSerializer.Serialize(r);
        return JsonSerializer.Deserialize<DownloadRecord>(json)!;
    }

    private async Task<List<DownloadRecord>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            var stream = File.OpenRead(_path);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonSerializer.DeserializeAsync<List<DownloadRecord>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
            }
        }
        catch (JsonException ex)
        {
            // Keep the damaged file for inspection and start clean instead of failing every request.
            var backup = _path + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
            File.Move(_path, backup);
            _logger.LogError(ex, "Download list was damaged and has been moved aside");
            return [];
        }
    }

    private async Task SaveAsync(List<DownloadRecord> records, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        var stream = File.Create(temp);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, records, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, _path, overwrite: true);
    }
}
