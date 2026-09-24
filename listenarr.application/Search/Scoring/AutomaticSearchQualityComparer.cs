/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System.Text.RegularExpressions;

namespace Listenarr.Application.Search.Scoring;

/// <summary>
/// Provides a conservative quality tie-break for automatic search without changing
/// quality-profile scoring or rejection behavior.
/// </summary>
public static class AutomaticSearchQualityComparer
{
    private static readonly Regex BitratePattern = new(
        @"\b(?<bitrate>\d{2,4})\s*(?:kbps|kbit/s|kb/s)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LosslessPattern = new(
        @"\b(?:flac|alac|aiff|ape|dsd|wav|wavpack|lossless)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public readonly record struct QualityRank(int Tier, int BitrateKbps);

    /// <summary>
    /// Returns the quality used only for automatic-search comparison. Explicit indexer quality
    /// always wins; title parsing is a fallback only for blank/unknown quality.
    /// </summary>
    public static string? ResolveEffectiveQuality(SearchResult result)
        => ResolveEffectiveQuality(result.Quality, result.Title);

    public static string? ResolveEffectiveQuality(string? explicitQuality, string? title)
    {
        if (!IsUnknown(explicitQuality))
        {
            return explicitQuality!.Trim();
        }

        return SearchResultAttributeParser.DetectQualityFromTitle(title);
    }

    /// <summary>
    /// Orders candidates by score first, then by conservative audio quality. LINQ's original
    /// position is retained as the final key so results remain stable when quality is unknown
    /// or otherwise not comparable.
    /// </summary>
    public static IEnumerable<T> OrderByScoreThenQuality<T>(
        IEnumerable<T> candidates,
        Func<T, int> scoreSelector,
        Func<T, SearchResult> resultSelector)
    {
        return candidates
            .Select((candidate, index) => new
            {
                Candidate = candidate,
                Index = index,
                Score = scoreSelector(candidate),
                Rank = GetRank(resultSelector(candidate))
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Rank.Tier)
            .ThenByDescending(x => x.Rank.BitrateKbps)
            .ThenBy(x => x.Index)
            .Select(x => x.Candidate);
    }

    public static QualityRank GetRank(SearchResult result)
        => GetRank(ResolveEffectiveQuality(result));

    public static QualityRank GetRank(string? quality)
    {
        if (IsUnknown(quality))
        {
            return default;
        }

        var normalized = quality!.Trim();
        if (LosslessPattern.IsMatch(normalized))
        {
            return new QualityRank(Tier: 2, BitrateKbps: 0);
        }

        var match = BitratePattern.Match(normalized);
        if (match.Success && int.TryParse(match.Groups["bitrate"].Value, out var bitrate))
        {
            return new QualityRank(Tier: 1, BitrateKbps: bitrate);
        }

        // A codec/container label without a trustworthy bitrate does not establish an ordering.
        return default;
    }

    public static bool IsBetter(SearchResult candidate, string? existingQuality)
    {
        var candidateRank = GetRank(candidate);
        if (candidateRank.Tier == 0)
        {
            return false;
        }

        if (IsUnknown(existingQuality))
        {
            return true;
        }

        var existingRank = GetRank(existingQuality);
        return existingRank.Tier != 0 && Compare(candidateRank, existingRank) > 0;
    }

    public static bool IsLabelBetter(string? candidateQuality, string? existingQuality)
    {
        var candidateRank = GetRank(candidateQuality);
        if (candidateRank.Tier == 0)
        {
            return false;
        }

        if (IsUnknown(existingQuality))
        {
            return true;
        }

        var existingRank = GetRank(existingQuality);
        return existingRank.Tier != 0 && Compare(candidateRank, existingRank) > 0;
    }

    private static int Compare(QualityRank candidate, QualityRank existing)
    {
        var tierComparison = candidate.Tier.CompareTo(existing.Tier);
        if (tierComparison != 0)
        {
            return tierComparison;
        }

        if (candidate.Tier == 1)
        {
            return candidate.BitrateKbps.CompareTo(existing.BitrateKbps);
        }

        return 0;
    }

    private static bool IsUnknown(string? quality)
        => string.IsNullOrWhiteSpace(quality)
            || string.Equals(quality.Trim(), "unknown", StringComparison.OrdinalIgnoreCase);
}
