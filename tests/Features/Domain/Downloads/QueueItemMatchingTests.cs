/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Tests.Features.Domain.Downloads
{
    public class QueueItemMatchingTests
    {
        [Fact]
        public void GetMatchScore_DifferentKnownClientId_DoesNotFallBackToExactTitle()
        {
            // Arrange
            var download = new Download
            {
                Id = "listenarr-download-1",
                Title = "Horus.Rising"
            };
            download.SetMetadata(
                "ClientDownloadId",
                "a4ab7327-5401-4614-8842-ae76a9523994");

            var queueItem = new QueueItem
            {
                Id = "e58bfa29-6813-415e-9808-458a2f532cdb",
                Title = "Horus.Rising"
            };

            // Act
            var score = queueItem.GetMatchScore(download);

            // Assert
            Assert.Equal(0, score);
        }

        [Fact]
        public void GetMatchScore_MatchingKnownClientId_ReturnsClientIdScore()
        {
            // Arrange
            var download = new Download
            {
                Id = "listenarr-download-1",
                Title = "Horus.Rising"
            };
            download.SetMetadata(
                "ClientDownloadId",
                "a4ab7327-5401-4614-8842-ae76a9523994");

            var queueItem = new QueueItem
            {
                Id = "a4ab7327-5401-4614-8842-ae76a9523994",
                Title = "Horus.Rising"
            };

            // Act
            var score = queueItem.GetMatchScore(download);

            // Assert
            Assert.Equal(3, score);
        }

        [Fact]
        public void GetMatchScore_NoKnownClientId_StillAllowsExactTitleFallback()
        {
            // Arrange
            var download = new Download
            {
                Id = "listenarr-download-1",
                Title = "Horus.Rising"
            };

            var queueItem = new QueueItem
            {
                Id = "some-external-client-id",
                Title = "Horus.Rising"
            };

            // Act
            var score = queueItem.GetMatchScore(download);

            // Assert
            Assert.Equal(2, score);
        }
    }
}
