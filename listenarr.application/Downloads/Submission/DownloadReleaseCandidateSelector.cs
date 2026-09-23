/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Application.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Downloads.Submission;

internal static class DownloadReleaseCandidateSelector
{
    public static async Task<(SearchResult? SearchResult, TrustedDownloadCandidate? Candidate, string? FailureMessage)> SelectAsync(
        int audiobookId,
        Audiobook audiobook,
        List<SearchResult>? searchResults,
        IQualityProfileService qualityProfileService,
        IDownloadRepository downloadRepository,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(audiobook);
        ArgumentNullException.ThrowIfNull(qualityProfileService);
        ArgumentNullException.ThrowIfNull(downloadRepository);
        ArgumentNullException.ThrowIfNull(logger);

        if (searchResults is null || searchResults.Count == 0)
        {
            return (null, null, "No search results found");
        }

        if (audiobook.QualityProfile is null)
        {
            throw new InvalidOperationException("Audiobook has no quality profile assigned");
        }

        var scoredResults = await qualityProfileService.ScoreSearchResults(searchResults, audiobook.QualityProfile);

        logger.LogInformation(
            "Scored {Count} search results for audiobook '{Title}':",
            scoredResults.Count,
            LogRedaction.SanitizeText(audiobook.Title));
        foreach (var scoredResult in scoredResults.OrderByDescending(s => s.TotalScore))
        {
            var status = scoredResult.IsRejected ? "REJECTED" : (scoredResult.TotalScore > 0 ? "ACCEPTABLE" : "LOW SCORE");
            logger.LogInformation(
                "  [{Status}] Score: {Score} | Title: {Title} | Source: {Source} | Size: {Size}MB | Seeders: {Seeders} | Quality: {Quality}",
                status,
                scoredResult.TotalScore,
                LogRedaction.SanitizeText(scoredResult.SearchResult.Title),
                LogRedaction.SanitizeText(scoredResult.SearchResult.Source),
                scoredResult.SearchResult.Size / 1024 / 1024,
                scoredResult.SearchResult.Seeders,
                scoredResult.SearchResult.Quality);
            if (scoredResult.IsRejected && scoredResult.RejectionReasons.Any())
            {
                logger.LogInformation("    Rejection reasons: {Reasons}", string.Join(", ", scoredResult.RejectionReasons));
            }
        }

        var acceptableResults = scoredResults
            .Where(s => !s.IsRejected && s.TotalScore > 0)
            .OrderByDescending(s => s.TotalScore)
            .ToList();

        if (acceptableResults.Count == 0)
        {
            logger.LogWarning(
                "No acceptable search results found for audiobook '{Title}' after quality filtering",
                audiobook.Title);
            return (null, null, "No acceptable search results found");
        }

        var existingDownloads = await downloadRepository.GetByAudiobookIdAsync(audiobookId);

        foreach (var scoredResult in acceptableResults)
        {
            scoredResult.SearchResult.Score = scoredResult.TotalScore;
            var candidate = TrustedDownloadCandidateFactory.Create(scoredResult.SearchResult);

            if (DownloadReleaseDuplicateGuard.WasAlreadyUsed(audiobookId, candidate, existingDownloads))
            {
                logger.LogInformation(
                    "Skipping previously used release for audiobook {AudiobookId}: '{Title}' ({ReleaseId})",
                    audiobookId,
                    LogRedaction.SanitizeText(candidate.Title),
                    LogRedaction.SanitizeText(candidate.Id));
                continue;
            }

            return (scoredResult.SearchResult, candidate, null);
        }

        logger.LogInformation(
            "All acceptable search results for audiobook {AudiobookId} were previously used; no download was sent",
            audiobookId);
        return (null, null, "No new acceptable releases found");
    }
}
