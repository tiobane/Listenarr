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

namespace Listenarr.Infrastructure.HostedServices.Search
{
    internal static class AutomaticSearchRuntimeGuard
    {
        private const double MinimumRuntimeRatio = 0.25d;

        private static readonly Regex BitratePattern = new(
            @"\b(?<bitrate>\d{2,4})\s*(?:kbps|kbit/s|kb/s)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal readonly record struct Evaluation(
            bool ShouldReject,
            int? BitrateKbps = null,
            double? EstimatedRuntimeMinutes = null,
            double? MinimumRuntimeMinutes = null);

        public static Evaluation Evaluate(SearchResult result, int? referenceRuntimeMinutes)
        {
            ArgumentNullException.ThrowIfNull(result);

            if (referenceRuntimeMinutes is null or <= 0 || result.Size <= 0 || !HasKnownAudioContext(result))
            {
                return default;
            }

            if (!TryGetBitrateKbps(result, out var bitrateKbps))
            {
                return default;
            }

            // SearchResult.Size is bytes. Audio bitrate is expressed in decimal kilobits/second.
            var estimatedRuntimeMinutes = result.Size * 8d / (bitrateKbps * 1000d * 60d);
            var minimumRuntimeMinutes = referenceRuntimeMinutes.Value * MinimumRuntimeRatio;

            return new Evaluation(
                ShouldReject: estimatedRuntimeMinutes < minimumRuntimeMinutes,
                BitrateKbps: bitrateKbps,
                EstimatedRuntimeMinutes: estimatedRuntimeMinutes,
                MinimumRuntimeMinutes: minimumRuntimeMinutes);
        }

        private static bool HasKnownAudioContext(SearchResult result)
            => IsKnown(result.Quality) || IsKnown(result.Format);

        private static bool IsKnown(string? value)
            => !string.IsNullOrWhiteSpace(value)
               && !string.Equals(value.Trim(), "unknown", StringComparison.OrdinalIgnoreCase);

        private static bool TryGetBitrateKbps(SearchResult result, out int bitrateKbps)
        {
            foreach (var value in new[] { result.Quality, result.Format, result.Title })
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var match = BitratePattern.Match(value);
                if (match.Success
                    && int.TryParse(match.Groups["bitrate"].Value, out bitrateKbps)
                    && bitrateKbps > 0)
                {
                    return true;
                }
            }

            bitrateKbps = 0;
            return false;
        }
    }
}
