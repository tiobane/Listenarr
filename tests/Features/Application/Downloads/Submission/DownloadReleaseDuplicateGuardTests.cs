using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Downloads.Submission;

[Trait("Name", nameof(DownloadReleaseDuplicateGuardTests))]
[Trait("Category", "Unit")]
public class DownloadReleaseDuplicateGuardTests : BaseTests
{
    [Fact]
    public void WasAlreadyUsed_MatchesPersistedReleaseIdForSameAudiobook()
    {
        var candidate = CreateUsenetCandidate(
            "release-123",
            "http://hydra/getnzb/api/111.-200?apikey=test",
            indexerId: 7);
        var existing = new Download
        {
            AudiobookId = 42,
            Status = DownloadStatus.Moved,
            OriginalUrl = "http://hydra/getnzb/api/111.-100?apikey=test",
            Metadata = new Dictionary<string, object>
            {
                ["Source"] = "NZBHydra2",
                [DownloadReleaseDuplicateGuard.ReleaseIdMetadataKey] = "release-123",
                [DownloadReleaseDuplicateGuard.IndexerIdMetadataKey] = "7"
            }
        };

        Assert.True(DownloadReleaseDuplicateGuard.WasAlreadyUsed(42, candidate, [existing]));
    }

    [Fact]
    public void WasAlreadyUsed_AllowsDifferentReleaseForSameAudiobook()
    {
        var candidate = CreateUsenetCandidate(
            "release-new",
            "http://hydra/getnzb/api/222.-200?apikey=test",
            indexerId: 7);
        var existing = new Download
        {
            AudiobookId = 42,
            Status = DownloadStatus.Moved,
            OriginalUrl = "http://hydra/getnzb/api/111.-100?apikey=test",
            Metadata = new Dictionary<string, object>
            {
                ["Source"] = "NZBHydra2",
                [DownloadReleaseDuplicateGuard.ReleaseIdMetadataKey] = "release-old",
                [DownloadReleaseDuplicateGuard.IndexerIdMetadataKey] = "7"
            }
        };

        Assert.False(DownloadReleaseDuplicateGuard.WasAlreadyUsed(42, candidate, [existing]));
    }

    [Fact]
    public void WasAlreadyUsed_MatchesNzbHydraStableTokenAcrossVolatileSuffixes()
    {
        var candidate = CreateUsenetCandidate(
            "volatile-guid-new",
            "http://192.168.100.20:5076/getnzb/api/5609286739899623127.-64598322?apikey=test",
            indexerId: 7);
        var existing = new Download
        {
            AudiobookId = 42,
            Status = DownloadStatus.Moved,
            OriginalUrl = "http://192.168.100.20:5076/getnzb/api/5609286739899623127.-64704522?apikey=test",
            Metadata = new Dictionary<string, object>
            {
                ["Source"] = "NZBHydra2",
                [DownloadReleaseDuplicateGuard.ReleaseIdMetadataKey] = "volatile-guid-old",
                [DownloadReleaseDuplicateGuard.IndexerIdMetadataKey] = "7"
            }
        };

        Assert.True(DownloadReleaseDuplicateGuard.WasAlreadyUsed(42, candidate, [existing]));
    }

    [Fact]
    public void WasAlreadyUsed_MatchesLegacyRowWithoutReleaseMetadata()
    {
        var candidate = CreateUsenetCandidate(
            "release-new",
            "http://hydra/getnzb/api/5609286739899623127.-64598322?apikey=test",
            indexerId: 7);
        var existing = new Download
        {
            AudiobookId = 42,
            Status = DownloadStatus.Moved,
            OriginalUrl = "http://hydra/getnzb/api/5609286739899623127.-64704522?apikey=test",
            Metadata = new Dictionary<string, object>
            {
                ["Source"] = "NZBHydra2"
            }
        };

        Assert.True(DownloadReleaseDuplicateGuard.WasAlreadyUsed(42, candidate, [existing]));
    }

    [Fact]
    public void WasAlreadyUsed_DoesNotMatchReleaseFromDifferentAudiobook()
    {
        var candidate = CreateUsenetCandidate(
            "release-123",
            "http://hydra/getnzb/api/111.-200?apikey=test",
            indexerId: 7);
        var existing = new Download
        {
            AudiobookId = 99,
            Status = DownloadStatus.Moved,
            OriginalUrl = "http://hydra/getnzb/api/111.-100?apikey=test",
            Metadata = new Dictionary<string, object>
            {
                ["Source"] = "NZBHydra2",
                [DownloadReleaseDuplicateGuard.ReleaseIdMetadataKey] = "release-123"
            }
        };

        Assert.False(DownloadReleaseDuplicateGuard.WasAlreadyUsed(42, candidate, [existing]));
    }

    [Fact]
    public void DownloadRecordFactory_PersistsReleaseIdentityMetadata()
    {
        var candidate = CreateUsenetCandidate(
            "release-123",
            "http://hydra/getnzb/api/111.-200?apikey=test",
            indexerId: 7);
        var submission = new PreparedUsenetSubmission(
            candidate.Title,
            candidate.Artist,
            candidate.Album,
            candidate.Source,
            candidate.Quality,
            candidate.Language,
            candidate.Size,
            candidate.SourceDescriptor.Locators[0].Value,
            [1, 2, 3],
            "release.nzb");
        var client = new DownloadClientConfiguration
        {
            Id = "sab-1",
            DownloadPath = "/downloads"
        };

        var download = DownloadRecordFactory.CreateQueuedDownload(
            "download-1",
            candidate,
            submission,
            client,
            client.Id,
            42);

        Assert.Equal("release-123", download.GetMetadataString(DownloadReleaseDuplicateGuard.ReleaseIdMetadataKey));
        Assert.Equal("7", download.GetMetadataString(DownloadReleaseDuplicateGuard.IndexerIdMetadataKey));
        Assert.Equal("Torznab", download.GetMetadataString(DownloadReleaseDuplicateGuard.IndexerImplementationMetadataKey));
    }

    private static TrustedDownloadCandidate CreateUsenetCandidate(
        string releaseId,
        string nzbUrl,
        int indexerId)
        => new(
            releaseId,
            "Zero Day",
            "David Baldacci",
            "Zero Day",
            "NZBHydra2",
            "MP3 32kbps",
            "English",
            123456789,
            0,
            new DownloadSourceDescriptor(
                indexerId,
                "Torznab",
                DownloadProtocol.Usenet,
                [new DownloadSourceLocator(DownloadSourceLocatorKind.NzbUrl, nzbUrl)],
                "release.nzb"));
}
