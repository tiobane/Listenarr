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
        var existing = CreateDownload(
            audiobookId: 42,
            status: DownloadStatus.Moved,
            releaseId: "release-123",
            originalUrl: "http://hydra/getnzb/api/111.-100?apikey=test");

        Assert.True(DownloadReleaseDuplicateGuard.WasAlreadyUsed(42, candidate, [existing]));
    }

    [Fact]
    public void WasAlreadyUsed_MatchesFailedPersistedReleaseIdForSameAudiobook()
    {
        var candidate = CreateUsenetCandidate(
            "release-123",
            "http://hydra/getnzb/api/111.-200?apikey=test",
            indexerId: 7);
        var existing = CreateDownload(
            audiobookId: 42,
            status: DownloadStatus.Failed,
            releaseId: "release-123",
            originalUrl: "http://hydra/getnzb/api/111.-100?apikey=test");

        Assert.True(DownloadReleaseDuplicateGuard.WasAlreadyUsed(42, candidate, [existing]));
    }

    [Fact]
    public void WasAlreadyUsed_AllowsDifferentReleaseForSameAudiobook()
    {
        var candidate = CreateUsenetCandidate(
            "release-new",
            "http://hydra/getnzb/api/222.-200?apikey=test",
            indexerId: 7);
        var existing = CreateDownload(
            audiobookId: 42,
            status: DownloadStatus.Failed,
            releaseId: "release-old",
            originalUrl: "http://hydra/getnzb/api/111.-100?apikey=test");

        Assert.False(DownloadReleaseDuplicateGuard.WasAlreadyUsed(42, candidate, [existing]));
    }

    [Fact]
    public void WasAlreadyUsed_MatchesNzbHydraStableTokenAcrossVolatileSuffixes()
    {
        var candidate = CreateUsenetCandidate(
            "volatile-guid-new",
            "http://192.168.100.20:5076/getnzb/api/5609286739899623127.-64598322?apikey=test",
            indexerId: 7);
        var existing = CreateDownload(
            audiobookId: 42,
            status: DownloadStatus.Failed,
            releaseId: null,
            originalUrl: "http://192.168.100.20:5076/getnzb/api/5609286739899623127.-64704522?apikey=test");

        Assert.True(DownloadReleaseDuplicateGuard.WasAlreadyUsed(42, candidate, [existing]));
    }

    [Fact]
    public void WasAlreadyUsed_MatchesLegacyRowWithoutReleaseMetadata()
    {
        var candidate = CreateUsenetCandidate(
            "release-new",
            "http://hydra/getnzb/api/5609286739899623127.-64598322?apikey=test",
            indexerId: 7);
        var existing = CreateDownload(
            audiobookId: 42,
            status: DownloadStatus.Moved,
            releaseId: null,
            originalUrl: "http://hydra/getnzb/api/5609286739899623127.-64704522?apikey=test");

        Assert.True(DownloadReleaseDuplicateGuard.WasAlreadyUsed(42, candidate, [existing]));
    }

    [Fact]
    public void WasAlreadyUsed_MatchesFailedLegacyRowByExactOriginalLocatorWhenHydraIdentityUnavailable()
    {
        const string originalUrl = "https://indexer.example/download/legacy-release-123.nzb?token=stable";
        var candidate = CreateUsenetCandidate(
            "new-guid-not-present-on-legacy-row",
            originalUrl,
            indexerId: 7);
        var existing = CreateDownload(
            audiobookId: 42,
            status: DownloadStatus.Failed,
            releaseId: null,
            originalUrl: originalUrl);

        Assert.True(DownloadReleaseDuplicateGuard.WasAlreadyUsed(42, candidate, [existing]));
    }

    [Fact]
    public void WasAlreadyUsed_DoesNotMatchReleaseFromDifferentAudiobook()
    {
        var candidate = CreateUsenetCandidate(
            "release-123",
            "http://hydra/getnzb/api/111.-200?apikey=test",
            indexerId: 7);
        var existing = CreateDownload(
            audiobookId: 99,
            status: DownloadStatus.Failed,
            releaseId: "release-123",
            originalUrl: "http://hydra/getnzb/api/111.-100?apikey=test");

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

    private static Download CreateDownload(
        int audiobookId,
        DownloadStatus status,
        string? releaseId,
        string originalUrl)
    {
        var metadata = new Dictionary<string, object>
        {
            ["Source"] = "NZBHydra2",
            [DownloadReleaseDuplicateGuard.IndexerIdMetadataKey] = "7"
        };

        if (!string.IsNullOrWhiteSpace(releaseId))
        {
            metadata[DownloadReleaseDuplicateGuard.ReleaseIdMetadataKey] = releaseId;
        }

        return new Download
        {
            AudiobookId = audiobookId,
            Status = status,
            OriginalUrl = originalUrl,
            Metadata = metadata
        };
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
