/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Application.Downloads.Submission;

public static class DownloadReleaseDuplicateGuard
{
    internal const string ReleaseIdMetadataKey = "ReleaseId";
    internal const string IndexerIdMetadataKey = "IndexerId";
    internal const string IndexerImplementationMetadataKey = "IndexerImplementation";

    public static bool WasAlreadyUsed(
        int audiobookId,
        TrustedDownloadCandidate candidate,
        IEnumerable<Download> existingDownloads)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existingDownloads);

        foreach (var existing in existingDownloads)
        {
            if (existing.AudiobookId != audiobookId || existing.Status == DownloadStatus.Failed)
            {
                continue;
            }

            var existingReleaseId = existing.GetMetadataString(ReleaseIdMetadataKey);
            if (!string.IsNullOrWhiteSpace(existingReleaseId) &&
                ReleaseIdMatches(candidate, existing, existingReleaseId))
            {
                return true;
            }

            // NZBHydra's GUID/download URL can contain a volatile suffix, so compare the
            // stable getnzb token as well. This also makes the guard work for legacy rows
            // that predate persisted ReleaseId metadata.
            if (NzbHydraReleaseMatches(candidate, existing))
            {
                return true;
            }
        }

        return false;
    }

    public static bool RepresentsSameRelease(SearchResult first, SearchResult second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (!SourcesMatch(first.Source, second.Source))
        {
            return false;
        }

        if (SearchResultReleaseIdsMatch(first, second))
        {
            return true;
        }

        if (SearchResultNzbHydraReleaseMatches(first, second))
        {
            return true;
        }

        return LocatorMatches(first.NzbUrl, second.NzbUrl) ||
               LocatorMatches(first.MagnetLink, second.MagnetLink) ||
               LocatorMatches(first.TorrentUrl, second.TorrentUrl);
    }

    private static bool SearchResultReleaseIdsMatch(SearchResult first, SearchResult second)
    {
        if (string.IsNullOrWhiteSpace(first.Id) ||
            string.IsNullOrWhiteSpace(second.Id) ||
            !string.Equals(first.Id.Trim(), second.Id.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !first.IndexerId.HasValue ||
               !second.IndexerId.HasValue ||
               first.IndexerId.Value == second.IndexerId.Value;
    }

    private static bool SearchResultNzbHydraReleaseMatches(SearchResult first, SearchResult second)
    {
        var firstToken = TryGetNzbHydraReleaseToken(first.NzbUrl);
        var secondToken = TryGetNzbHydraReleaseToken(second.NzbUrl);

        return !string.IsNullOrWhiteSpace(firstToken) &&
               !string.IsNullOrWhiteSpace(secondToken) &&
               string.Equals(firstToken, secondToken, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SourcesMatch(string? firstSource, string? secondSource)
    {
        return string.IsNullOrWhiteSpace(firstSource) ||
               string.IsNullOrWhiteSpace(secondSource) ||
               string.Equals(firstSource, secondSource, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LocatorMatches(string? firstLocator, string? secondLocator)
    {
        return !string.IsNullOrWhiteSpace(firstLocator) &&
               !string.IsNullOrWhiteSpace(secondLocator) &&
               string.Equals(firstLocator, secondLocator, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReleaseIdMatches(
        TrustedDownloadCandidate candidate,
        Download existing,
        string existingReleaseId)
    {
        if (string.IsNullOrWhiteSpace(candidate.Id) ||
            !string.Equals(existingReleaseId, candidate.Id, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var existingIndexerId = existing.GetMetadataString(IndexerIdMetadataKey);
        var candidateIndexerId = candidate.SourceDescriptor.IndexerId?.ToString();
        if (!string.IsNullOrWhiteSpace(existingIndexerId) &&
            !string.IsNullOrWhiteSpace(candidateIndexerId) &&
            !string.Equals(existingIndexerId, candidateIndexerId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var existingSource = existing.GetMetadataString("Source");
        return string.IsNullOrWhiteSpace(existingSource) ||
               string.IsNullOrWhiteSpace(candidate.Source) ||
               string.Equals(existingSource, candidate.Source, StringComparison.OrdinalIgnoreCase);
    }

    private static bool NzbHydraReleaseMatches(
        TrustedDownloadCandidate candidate,
        Download existing)
    {
        var existingSource = existing.GetMetadataString("Source");
        if (!string.IsNullOrWhiteSpace(existingSource) &&
            !string.IsNullOrWhiteSpace(candidate.Source) &&
            !string.Equals(existingSource, candidate.Source, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var existingToken = TryGetNzbHydraReleaseToken(existing.OriginalUrl);
        if (string.IsNullOrWhiteSpace(existingToken))
        {
            return false;
        }

        return candidate.SourceDescriptor.Locators
            .Where(locator => locator.Kind == DownloadSourceLocatorKind.NzbUrl)
            .Select(locator => TryGetNzbHydraReleaseToken(locator.Value))
            .Any(candidateToken =>
                !string.IsNullOrWhiteSpace(candidateToken) &&
                string.Equals(existingToken, candidateToken, StringComparison.OrdinalIgnoreCase));
    }

    internal static string? TryGetNzbHydraReleaseToken(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        const string marker = "/getnzb/api/";
        var markerIndex = uri.AbsolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var token = Uri.UnescapeDataString(uri.AbsolutePath[(markerIndex + marker.Length)..]).Trim('/');
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        // NZBHydra may append a volatile suffix such as '.-64598322' to the stable
        // release identifier. Ignore that suffix so repeated links map to one release.
        var volatileSuffixIndex = token.IndexOf(".-", StringComparison.Ordinal);
        return volatileSuffixIndex > 0 ? token[..volatileSuffixIndex] : token;
    }
}
