using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Processing;

[Trait("Name", nameof(UnusableReleaseImportRecoveryTests))]
[Trait("Category", "DownloadProcessingJob")]
public class UnusableReleaseImportRecoveryTests : BaseTests
{
    private readonly DownloadClientGatewayMock downloadClientGatewayMock = new();
    private readonly Mock<IDownloadImportService> downloadImportServiceMock = new();

    public override async Task InitializeAsync()
    {
        _services.AddSingleton<IDownloadClientGateway>(downloadClientGatewayMock);
        downloadImportServiceMock
            .Setup(service => service.ImportDownloadFilesAsync(
                It.IsAny<Audiobook>(),
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<DownloadImportOptions?>()))
            .ReturnsAsync((Audiobook _, List<string> files, CancellationToken _, DownloadImportOptions? _) =>
                [ImportResult.ImportSuccess(FileAction.Copy, files[0], files[0], wasRegisteredToAudiobook: false)]);

        Init(builder => builder.WithSingleton<IDownloadImportService>(downloadImportServiceMock.Object));
        await AddAuthorizedRootAsync(FileService.GetTempPath());
    }

    [Fact]
    public async Task CompletedReleaseWithoutRegisteredAudio_IsFailedAndMarkedUnusable()
    {
        var source = FileService.GetTempDirectory("wrong-release-payload");
        var payload = await FileService.GetFileAsync(
            source,
            "Spartacus.House.of.Ashur.S01E10.1080p.mkv");
        downloadClientGatewayMock.SourceFiles = [payload];

        var audiobook = await CreateAudiobook();
        var download = await _downloadRepository.AddAsync(new DownloadBuilder()
            .WithCompletedStatus(DateTime.UtcNow)
            .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
            .WithAudiobook(audiobook)
            .WithPath(source)
            .Build());
        var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
            .WithDownload(download)
            .Build());

        await _provider.GetRequiredService<DownloadProcessingJobProcessor>()
            .ProcessQueueAsync(CancellationToken.None);

        job = (await _downloadProcessingJobRepository.GetByIdAsync(job.Id))!;
        Assert.Equal(ProcessingJobStatus.Failed, job.Status);
        Assert.True(job.TryGetJobDataString(
            DownloadReleaseDuplicateGuard.UnusableReleaseMetadataKey,
            out var unusableJobValue));
        Assert.True(bool.Parse(unusableJobValue));

        download = (await _downloadRepository.GetByIdAsync(download.Id))!;
        Assert.Equal(DownloadStatus.Failed, download.Status);
        Assert.False(download.IsBlocked());
        Assert.Equal(
            bool.TrueString,
            download.GetMetadataString(DownloadReleaseDuplicateGuard.UnusableReleaseMetadataKey),
            ignoreCase: true);
        Assert.Contains("No audio files were registered", download.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailedReleaseMarkedUnusable_RemainsDeduplicated()
    {
        var candidate = CreateUsenetCandidate("release-123", "http://hydra/getnzb/api/111.-200?apikey=test");
        var failedDownload = CreateFailedDownload("release-123", unusable: true);

        Assert.True(DownloadReleaseDuplicateGuard.WasAlreadyUsed(42, candidate, [failedDownload]));
    }

    [Fact]
    public void OrdinaryFailedRelease_RemainsRetryable()
    {
        var candidate = CreateUsenetCandidate("release-123", "http://hydra/getnzb/api/111.-200?apikey=test");
        var failedDownload = CreateFailedDownload("release-123", unusable: false);

        Assert.False(DownloadReleaseDuplicateGuard.WasAlreadyUsed(42, candidate, [failedDownload]));
    }

    private static Download CreateFailedDownload(string releaseId, bool unusable)
    {
        var metadata = new Dictionary<string, object>
        {
            ["Source"] = "NZBHydra2",
            [DownloadReleaseDuplicateGuard.ReleaseIdMetadataKey] = releaseId,
            [DownloadReleaseDuplicateGuard.IndexerIdMetadataKey] = "7"
        };
        if (unusable)
        {
            metadata[DownloadReleaseDuplicateGuard.UnusableReleaseMetadataKey] = true;
        }

        return new Download
        {
            AudiobookId = 42,
            Status = DownloadStatus.Failed,
            OriginalUrl = "http://hydra/getnzb/api/111.-100?apikey=test",
            Metadata = metadata
        };
    }

    private static TrustedDownloadCandidate CreateUsenetCandidate(string releaseId, string nzbUrl)
        => new(
            releaseId,
            "Die Drei Fragezeichen-47 Und Der Giftige Gockel",
            "Die Drei ???",
            "Die Drei Fragezeichen-47 Und Der Giftige Gockel",
            "NZBHydra2",
            "MP3",
            "German",
            123456789,
            0,
            new DownloadSourceDescriptor(
                7,
                "Torznab",
                DownloadProtocol.Usenet,
                [new DownloadSourceLocator(DownloadSourceLocatorKind.NzbUrl, nzbUrl)],
                "release.nzb"));
}
