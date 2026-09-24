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

using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.Scoring
{
    [Trait("Name", "AutomaticSearchQualityComparerTests")]
    [Trait("Category", "Search")]
    public class AutomaticSearchQualityComparerTests : BaseTests
    {
        private sealed record Candidate(int Score, SearchResult SearchResult);

        [Fact]
        public void ResolveEffectiveQuality_PrefersExplicitIndexerQualityOverTitleFallback()
        {
            var result = new SearchResult
            {
                Quality = "AAC 64kbps",
                Title = "Book Title 320 kbps"
            };

            Assert.Equal("AAC 64kbps", AutomaticSearchQualityComparer.ResolveEffectiveQuality(result));
        }

        [Fact]
        public void ResolveEffectiveQuality_UsesTitleFallbackForUnknownQuality()
        {
            var result = new SearchResult
            {
                Quality = "Unknown",
                Title = "Book Title NMR 32 kbps"
            };

            Assert.Equal("32kbps", AutomaticSearchQualityComparer.ResolveEffectiveQuality(result));
        }

        [Fact]
        public void OrderByScoreThenQuality_HigherScoreAlwaysWins()
        {
            var lowerQualityHigherScore = new Candidate(90, new SearchResult { Quality = "AAC 64kbps", Title = "Low quality" });
            var higherQualityLowerScore = new Candidate(80, new SearchResult { Quality = "MP3 320kbps", Title = "High quality" });

            var ordered = AutomaticSearchQualityComparer.OrderByScoreThenQuality(
                new[] { higherQualityLowerScore, lowerQualityHigherScore },
                candidate => candidate.Score,
                candidate => candidate.SearchResult).ToList();

            Assert.Same(lowerQualityHigherScore, ordered[0]);
        }

        [Fact]
        public void OrderByScoreThenQuality_UsesBitrateAcrossCodecBlocksOnlyForEqualScores()
        {
            var aac64 = new Candidate(80, new SearchResult { Quality = "AAC 64kbps", Title = "AAC release" });
            var mp3320 = new Candidate(80, new SearchResult { Quality = "MP3 320kbps", Title = "MP3 release" });

            var ordered = AutomaticSearchQualityComparer.OrderByScoreThenQuality(
                new[] { aac64, mp3320 },
                candidate => candidate.Score,
                candidate => candidate.SearchResult).ToList();

            Assert.Same(mp3320, ordered[0]);
        }

        [Fact]
        public void OrderByScoreThenQuality_UsesTitleBitrateFallbackWhenExplicitQualityIsMissing()
        {
            var bitrate32 = new Candidate(80, new SearchResult { Title = "Book NMR 32 kbps" });
            var bitrate128 = new Candidate(80, new SearchResult { Title = "Book 128 kbps" });

            var ordered = AutomaticSearchQualityComparer.OrderByScoreThenQuality(
                new[] { bitrate32, bitrate128 },
                candidate => candidate.Score,
                candidate => candidate.SearchResult).ToList();

            Assert.Same(bitrate128, ordered[0]);
        }

        [Fact]
        public void OrderByScoreThenQuality_LosslessWinsEqualScoreAgainstLossy()
        {
            var lossy = new Candidate(80, new SearchResult { Quality = "MP3 320kbps", Title = "MP3 release" });
            var lossless = new Candidate(80, new SearchResult { Quality = "FLAC", Title = "FLAC release" });

            var ordered = AutomaticSearchQualityComparer.OrderByScoreThenQuality(
                new[] { lossy, lossless },
                candidate => candidate.Score,
                candidate => candidate.SearchResult).ToList();

            Assert.Same(lossless, ordered[0]);
        }

        [Fact]
        public void OrderByScoreThenQuality_PreservesInputOrderWhenQualityIsUnknown()
        {
            var first = new Candidate(80, new SearchResult { Title = "First release" });
            var second = new Candidate(80, new SearchResult { Title = "Second release" });

            var ordered = AutomaticSearchQualityComparer.OrderByScoreThenQuality(
                new[] { first, second },
                candidate => candidate.Score,
                candidate => candidate.SearchResult).ToList();

            Assert.Same(first, ordered[0]);
            Assert.Same(second, ordered[1]);
        }

        [Fact]
        public void IsLabelBetter_UsesBitrateInsteadOfCodecBlockPriority()
        {
            Assert.True(AutomaticSearchQualityComparer.IsLabelBetter("MP3 320kbps", "AAC 64kbps"));
            Assert.False(AutomaticSearchQualityComparer.IsLabelBetter("AAC 64kbps", "MP3 320kbps"));
        }

        [Fact]
        public void IsLabelBetter_DoesNotGuessAgainstKnownUnrankedQuality()
        {
            Assert.False(AutomaticSearchQualityComparer.IsLabelBetter("MP3 64kbps", "MP3 VBR"));
        }
    }
}
