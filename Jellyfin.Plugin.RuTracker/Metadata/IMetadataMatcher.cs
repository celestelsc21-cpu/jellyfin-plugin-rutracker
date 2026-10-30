using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RuTracker.Api.Models;
using Jellyfin.Plugin.RuTracker.Configuration;

namespace Jellyfin.Plugin.RuTracker.Metadata;

/// <summary>
/// Matches releases with movies and series of the metadata providers connected to Jellyfin.
/// </summary>
public interface IMetadataMatcher
{
    /// <summary>
    /// Suggests matches for a release title (original and Russian names are tried).
    /// </summary>
    /// <param name="kind">Release type.</param>
    /// <param name="releaseTitle">RuTracker topic title.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matches, best first; empty when nothing was found or no provider is enabled.</returns>
    Task<IReadOnlyList<MetadataMatchDto>> SuggestAsync(MediaKind kind, string releaseTitle, CancellationToken cancellationToken);

    /// <summary>
    /// Searches by a name typed by the user.
    /// </summary>
    /// <param name="kind">Release type.</param>
    /// <param name="name">Name.</param>
    /// <param name="year">Optional year.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matches.</returns>
    Task<IReadOnlyList<MetadataMatchDto>> SearchAsync(MediaKind kind, string name, int? year, CancellationToken cancellationToken);
}
