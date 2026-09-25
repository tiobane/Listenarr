/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.HostedServices.Search
{
    internal sealed record AutomaticSearchQueryVariant(string Name, string Query);

    internal sealed class AutomaticSearchResultClassifier
    {
        private readonly ILogger _logger;

        public AutomaticSearchResultClassifier(ILogger logger)
        {
            _logger = logger;
        }

        public string BuildSearchQuery(Audiobook audiobook)
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(audiobook.Title))
                parts.Add(audiobook.Title);

            if (audiobook.Authors != null && audiobook.Authors.Any())
                parts.Add(audiobook.Authors.First());

            if (!string.IsNullOrEmpty(audiobook.Series))
                parts.Add(audiobook.Series);

            return string.Join(" ", parts);
        }

        public IReadOnlyList<AutomaticSearchQueryVariant> BuildSearchQueries(Audiobook audiobook)
        {
            ArgumentNullException.ThrowIfNull(audiobook);

            var variants = new List<AutomaticSearchQueryVariant>();
            var preciseQuery = BuildSearchQuery(audiobook);

            if (!string.IsNullOrWhiteSpace(preciseQuery))
            {
                variants.Add(new AutomaticSearchQueryVariant("Precise", preciseQuery));
            }

            var primaryAuthor = audiobook.Authors?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(audiobook.Title) &&
                !string.IsNullOrWhiteSpace(primaryAuthor))
            {
                var broadQuery = $"{audiobook.Title} {primaryAuthor}";
                if (!variants.Any(variant =>
                        string.Equals(variant.Query, broadQuery, StringComparison.OrdinalIgnoreCase)))
                {
                    variants.Add(new AutomaticSearchQueryVariant("Broad", broadQuery));
                }
            }

            return variants;
        }

        public List<SearchResult> MergeUniqueResults(IEnumerable<SearchResult> searchResults)
        {
            ArgumentNullException.ThrowIfNull(searchResults);

            var uniqueResults = new List<SearchResult>();

            foreach (var result in searchResults)
            {
                if (uniqueResults.Any(existing =>
                        DownloadReleaseDuplicateGuard.RepresentsSameRelease(existing, result)))
                {
                    continue;
                }

                uniqueResults.Add(result);
            }

            return uniqueResults;
        }

        public bool IsTorrentResult(SearchResult result)
        {
            if (!string.IsNullOrEmpty(result.DownloadType))
            {
                if (string.Equals(result.DownloadType, DirectDownloadMetadataKeys.ClientId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                else if (result.DownloadType == "Torrent")
                {
                    return true;
                }
                else if (result.DownloadType == "Usenet")
                {
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(result.NzbUrl))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(result.MagnetLink) || !string.IsNullOrEmpty(result.TorrentUrl))
            {
                return true;
            }

            _logger.LogWarning("Unable to determine result type for '{Title}' from source '{Source}'. No MagnetLink, TorrentUrl, or NzbUrl found. Defaulting to NZB.",
                result.Title, result.Source);
            return false;
        }
    }
}
