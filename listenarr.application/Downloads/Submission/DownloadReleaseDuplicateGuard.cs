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

internal static class DownloadReleaseDuplicateGuard
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
            if (!string.IsNullOrWhiteSpace(existingReleaseId))
            {
                if (ReleaseIdMatches(candidate, existing, existingReleaseId))
                {
                    return true;
                }

                // A persisted release identity is authoritative. Do not fall back to URL
                // heuristics when it explicitly identifies a different release.
                continue;
            }

            if (LegacyNzbHydraReleaseMatches(candidate, existing))
            {
                return true;
            }
        }

        return false;
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

    private static bool LegacyNzbHydraReleaseMatches(
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
        // release identifier. Ignore that suffix so legacy downloads can be matched.
        var volatileSuffixIndex = token.IndexOf(".-", StringComparison.Ordinal);
        return volatileSuffixIndex > 0 ? token[..volatileSuffixIndex] : token;
    }
}
