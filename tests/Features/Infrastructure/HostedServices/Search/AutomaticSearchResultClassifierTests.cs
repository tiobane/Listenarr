/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search
{
    [Trait("Name", "AutomaticSearchResultClassifierTests")]
    [Trait("Category", "Search")]
    public class AutomaticSearchResultClassifierTests : BaseTests
    {
        private readonly AutomaticSearchResultClassifier _classifier = new(Mock.Of<ILogger>());

        [Fact]
        public void BuildSearchQueries_ProducesPreciseAndBroadVariants()
        {
            var audiobook = new Audiobook
            {
                Title = "Zero Day",
                Authors = ["David Baldacci"],
                Series = "John Puller"
            };

            var variants = _classifier.BuildSearchQueries(audiobook);

            Assert.Collection(
                variants,
                variant =>
                {
                    Assert.Equal("Precise", variant.Name);
                    Assert.Equal("Zero Day David Baldacci John Puller", variant.Query);
                },
                variant =>
                {
                    Assert.Equal("Broad", variant.Name);
                    Assert.Equal("Zero Day David Baldacci", variant.Query);
                });
        }

        [Fact]
        public void BuildSearchQueries_DoesNotAddTitleOnlyFallback()
        {
            var audiobook = new Audiobook
            {
                Title = "Zero Day",
                Authors = ["David Baldacci"],
                Series = "John Puller"
            };

            var variants = _classifier.BuildSearchQueries(audiobook);

            Assert.DoesNotContain(variants, variant => variant.Query == "Zero Day");
            Assert.Equal(2, variants.Count);
        }

        [Fact]
        public void BuildSearchQueries_DoesNotRepeatBroadQueryWhenPreciseQueryIsAlreadyBroad()
        {
            var audiobook = new Audiobook
            {
                Title = "Zero Day",
                Authors = ["David Baldacci"]
            };

            var variants = _classifier.BuildSearchQueries(audiobook);

            var variant = Assert.Single(variants);
            Assert.Equal("Precise", variant.Name);
            Assert.Equal("Zero Day David Baldacci", variant.Query);
        }

        [Fact]
        public void MergeUniqueResults_MergesBothQueriesWithoutDuplicateRelease()
        {
            var releaseA = CreateResult("A", "Release A");
            var releaseBFromPrecise = CreateResult("B", "Release B");
            var releaseBFromBroad = CreateResult("B", "Release B");
            var releaseC = CreateResult("C", "Release C");

            var merged = _classifier.MergeUniqueResults(
                [releaseA, releaseBFromPrecise, releaseBFromBroad, releaseC]);

            Assert.Collection(
                merged,
                result => Assert.Equal("A", result.Id),
                result => Assert.Equal("B", result.Id),
                result => Assert.Equal("C", result.Id));
        }

        [Fact]
        public void MergeUniqueResults_UsesStableNzbHydraIdentityAcrossVolatileUrls()
        {
            var first = CreateResult("first-guid", "Release B");
            first.NzbUrl = "https://hydra.example/getnzb/api/release-token.-12345?apikey=one";

            var second = CreateResult("second-guid", "Release B");
            second.NzbUrl = "https://hydra.example/getnzb/api/release-token.-67890?apikey=two";

            var merged = _classifier.MergeUniqueResults([first, second]);

            Assert.Single(merged);
            Assert.Same(first, merged[0]);
        }

        [Fact]
        public void MergeUniqueResults_PreservesBroadOnlyCandidateForQualityTieBreaking()
        {
            var preciseCandidate = CreateResult("precise", "Zero Day NMR 32 kbps");
            preciseCandidate.Quality = "Unknown";
            preciseCandidate.Score = 80;

            var broadOnlyCandidate = CreateResult("broad", "1320 - David Baldacci - Puller 01 Zero Day.nzb");
            broadOnlyCandidate.Quality = "MP3 320kbps";
            broadOnlyCandidate.Score = 80;

            var merged = _classifier.MergeUniqueResults([preciseCandidate, broadOnlyCandidate]);
            var ordered = AutomaticSearchQualityComparer.OrderByScoreThenQuality(
                merged,
                result => result.Score,
                result => result).ToList();

            Assert.Equal(2, merged.Count);
            Assert.Same(broadOnlyCandidate, ordered[0]);
        }

        [Fact]
        public void MergeUniqueResults_DoesNotLetQualityOverrideHigherScore()
        {
            var higherScoreLowerQuality = CreateResult("precise", "Zero Day NMR 32 kbps");
            higherScoreLowerQuality.Quality = "MP3 32kbps";
            higherScoreLowerQuality.Score = 90;

            var lowerScoreHigherQuality = CreateResult("broad", "Zero Day 320 kbps");
            lowerScoreHigherQuality.Quality = "MP3 320kbps";
            lowerScoreHigherQuality.Score = 80;

            var merged = _classifier.MergeUniqueResults([higherScoreLowerQuality, lowerScoreHigherQuality]);
            var ordered = AutomaticSearchQualityComparer.OrderByScoreThenQuality(
                merged,
                result => result.Score,
                result => result).ToList();

            Assert.Same(higherScoreLowerQuality, ordered[0]);
        }

        private static SearchResult CreateResult(string id, string title)
        {
            return new SearchResult
            {
                Id = id,
                Title = title,
                Source = "NZBHydra2",
                IndexerId = 1,
                DownloadType = "Usenet",
                NzbUrl = $"https://hydra.example/getnzb/api/{id}"
            };
        }
    }
}
