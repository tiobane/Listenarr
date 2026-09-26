namespace Listenarr.Tests.Features.Infrastructure.Downloads.Submission;

[Trait("Name", "GenericUsenetSourceResolverTests")]
[Trait("Category", "GenericUsenetSourceResolver")]
public sealed class GenericUsenetSourceResolverTests : BaseTests
{
    private const string NzbUrl = "https://example.com/release.nzb";

    [Fact]
    public async Task ResolveAsync_ReleaseTitleWithQuotes_ProducesMultipartSafeFileName()
    {
        var downloader = CreateDownloader();
        var resolver = new GenericUsenetSourceResolver(downloader.Object);
        var candidate = CreateCandidate(
            "(01/14) - Description - \"Die drei Fragezeichen - Kai Schwind liest... und das Bergmonster.par2\" - 559,87 MB - yEnc");

        var prepared = await resolver.ResolveAsync(candidate, null, CancellationToken.None);

        var usenet = Assert.IsType<PreparedUsenetSubmission>(prepared);
        Assert.Equal(
            "(01_14) - Description - _Die drei Fragezeichen - Kai Schwind liest... und das Bergmonster.par2_ - 559,87 MB - yEnc.nzb",
            usenet.FileName);

        using var multipart = new MultipartFormDataContent();
        using var content = new ByteArrayContent(usenet.NzbBytes);
        multipart.Add(content, "name", usenet.FileName);
    }

    [Fact]
    public async Task ResolveAsync_ExplicitFileName_SanitizesPortableAndControlCharacters()
    {
        var downloader = CreateDownloader();
        var resolver = new GenericUsenetSourceResolver(downloader.Object);
        var candidate = CreateCandidate("Book", "unsafe:\"name\r\n?.nzb");

        var prepared = await resolver.ResolveAsync(candidate, null, CancellationToken.None);

        var usenet = Assert.IsType<PreparedUsenetSubmission>(prepared);
        Assert.Equal("unsafe__name___.nzb", usenet.FileName);
    }

    private static Mock<INzbFileDownloader> CreateDownloader()
    {
        var downloader = new Mock<INzbFileDownloader>();
        downloader
            .Setup(instance => instance.DownloadAsync(NzbUrl, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2, 3 });
        return downloader;
    }

    private static TrustedDownloadCandidate CreateCandidate(string title, string? fileName = null) => new(
        "release-id",
        title,
        "Artist",
        "Album",
        "NZBHydra2",
        "FLAC",
        "de",
        100,
        null,
        new DownloadSourceDescriptor(
            IndexerId: null,
            IndexerImplementation: "Newznab",
            Protocol: DownloadProtocol.Usenet,
            Locators:
            [
                new DownloadSourceLocator(
                    DownloadSourceLocatorKind.NzbUrl,
                    NzbUrl)
            ],
            FileName: fileName));
}
