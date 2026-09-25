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
    [Trait("Name", "AutomaticSearchRuntimeGuardTests")]
    [Trait("Category", "Search")]
    public class AutomaticSearchRuntimeGuardTests : BaseTests
    {
        [Fact]
        public void Evaluate_RejectsCandidateBelowQuarterOfReferenceRuntime()
        {
            var result = CreateResult(runtimeMinutes: 100, bitrateKbps: 128);

            var evaluation = AutomaticSearchRuntimeGuard.Evaluate(result, referenceRuntimeMinutes: 600);

            Assert.True(evaluation.ShouldReject);
            Assert.Equal(128, evaluation.BitrateKbps);
            Assert.Equal(100d, evaluation.EstimatedRuntimeMinutes!.Value, 6);
            Assert.Equal(150d, evaluation.MinimumRuntimeMinutes!.Value, 6);
        }

        [Fact]
        public void Evaluate_AllowsCandidateAtExactQuarterBoundary()
        {
            var result = CreateResult(runtimeMinutes: 150, bitrateKbps: 128);

            var evaluation = AutomaticSearchRuntimeGuard.Evaluate(result, referenceRuntimeMinutes: 600);

            Assert.False(evaluation.ShouldReject);
            Assert.Equal(150d, evaluation.EstimatedRuntimeMinutes!.Value, 6);
            Assert.Equal(150d, evaluation.MinimumRuntimeMinutes!.Value, 6);
        }

        [Fact]
        public void Evaluate_UsesTitleBitrateWhenAudioContextIsKnown()
        {
            var result = CreateResult(runtimeMinutes: 100, bitrateKbps: 128);
            result.Quality = "MP3";
            result.Title = "Example Audiobook 128 kbps";

            var evaluation = AutomaticSearchRuntimeGuard.Evaluate(result, referenceRuntimeMinutes: 600);

            Assert.True(evaluation.ShouldReject);
            Assert.Equal(128, evaluation.BitrateKbps);
        }

        [Fact]
        public void Evaluate_FailsOpenWhenReferenceRuntimeIsMissing()
        {
            var result = CreateResult(runtimeMinutes: 10, bitrateKbps: 128);

            var evaluation = AutomaticSearchRuntimeGuard.Evaluate(result, referenceRuntimeMinutes: null);

            Assert.False(evaluation.ShouldReject);
            Assert.Null(evaluation.BitrateKbps);
        }

        [Fact]
        public void Evaluate_FailsOpenWhenSizeIsMissing()
        {
            var result = CreateResult(runtimeMinutes: 10, bitrateKbps: 128);
            result.Size = 0;

            var evaluation = AutomaticSearchRuntimeGuard.Evaluate(result, referenceRuntimeMinutes: 600);

            Assert.False(evaluation.ShouldReject);
            Assert.Null(evaluation.BitrateKbps);
        }

        [Fact]
        public void Evaluate_FailsOpenForUnknownMetadataEvenWhenTitleContainsBitrate()
        {
            var result = CreateResult(runtimeMinutes: 10, bitrateKbps: 128);
            result.Quality = "Unknown";
            result.Format = "Unknown";
            result.Title = "Example Audiobook 128 kbps";

            var evaluation = AutomaticSearchRuntimeGuard.Evaluate(result, referenceRuntimeMinutes: 600);

            Assert.False(evaluation.ShouldReject);
            Assert.Null(evaluation.BitrateKbps);
        }

        [Fact]
        public void Evaluate_FailsOpenForM4bWithoutNumericBitrate()
        {
            var result = new SearchResult
            {
                Title = "Example Audiobook M4B",
                Format = "M4B",
                Quality = "M4B",
                Size = 10_000_000
            };

            var evaluation = AutomaticSearchRuntimeGuard.Evaluate(result, referenceRuntimeMinutes: 600);

            Assert.False(evaluation.ShouldReject);
            Assert.Null(evaluation.BitrateKbps);
        }

        private static SearchResult CreateResult(int runtimeMinutes, int bitrateKbps)
        {
            var sizeBytes = (long)(bitrateKbps * 1000L * runtimeMinutes * 60L / 8L);

            return new SearchResult
            {
                Title = "Example Audiobook",
                Format = "MP3",
                Quality = $"MP3 {bitrateKbps}kbps",
                Size = sizeBytes
            };
        }
    }
}
