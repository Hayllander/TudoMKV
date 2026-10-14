using TudoMKV.Core;
using TudoMKV.Core.Models;
using TudoMKV.Disc.Dvd;
using TudoMKV.Muxing;

namespace TudoMKV.Tests;

public sealed class DvdTests(TestDvd dvd) : IClassFixture<TestDvd>, IDisposable
{
    private readonly string _output = Directory.CreateTempSubdirectory("tudomkv-out-").FullName;

    public void Dispose() => Directory.Delete(_output, recursive: true);

    private void SkipIfUnavailable() => Assert.SkipWhen(dvd.SkipReason is not null, dvd.SkipReason ?? string.Empty);

    private string VideoTsIfo => Path.Combine(dvd.Root, "VIDEO_TS", "VIDEO_TS.IFO");

    [Fact]
    public void ScansTitlesFromFolder()
    {
        SkipIfUnavailable();

        var disc = DvdScanner.Scan(dvd.Root, TimeSpan.Zero);

        Assert.Equal("TESTE_DVD", disc.Label);
        Assert.Equal(dvd.Root, disc.InputPath);
        Assert.Equal(2, disc.Titles.Count);
        Assert.Equal(2, disc.Titles[0].Chapters);
        Assert.Equal(2, disc.Titles[1].TitleSet);
        Assert.InRange(disc.Titles[0].Duration.TotalSeconds, 7.5, 8.5);
        Assert.InRange(disc.Titles[1].Duration.TotalSeconds, 2.5, 3.5);
        Assert.True(disc.Titles[0].Cells >= 1);

        // O tamanho vem das células e bate com o VOB do título.
        var vob = new FileInfo(Path.Combine(dvd.Root, "VIDEO_TS", "VTS_01_1.VOB")).Length;
        Assert.InRange(disc.Titles[0].SizeBytes, vob * 0.9, vob * 1.1);
    }

    [Theory]
    [InlineData("VIDEO_TS.IFO")]
    [InlineData("pasta VIDEO_TS")]
    public void AcceptsTheIfoFileOrTheVideoTsFolder(string what)
    {
        SkipIfUnavailable();
        var path = what == "VIDEO_TS.IFO" ? VideoTsIfo : Path.Combine(dvd.Root, "VIDEO_TS");

        var disc = DvdScanner.Scan(path, TimeSpan.Zero);

        Assert.Equal(dvd.Root, disc.InputPath);
        Assert.Equal(2, disc.Titles.Count);
        Assert.True(DvdScanner.LooksLikeDvd(path));
    }

    [Fact]
    public void ScansTitlesFromIso()
    {
        SkipIfUnavailable();
        Assert.SkipWhen(dvd.IsoPath is null, "genisoimage não encontrado");

        var disc = DvdScanner.Scan(dvd.IsoPath!, TimeSpan.Zero);

        Assert.Equal("TESTE_ISO", disc.Label);
        Assert.Equal(2, disc.Titles.Count);
        Assert.InRange(disc.Titles[0].Duration.TotalSeconds, 7.5, 8.5);
    }

    [Fact]
    public void SkipsShortTitlesAndSaysWhy()
    {
        SkipIfUnavailable();

        var disc = DvdScanner.Scan(dvd.Root, TimeSpan.FromSeconds(5));

        var title = Assert.Single(disc.Titles);
        Assert.Equal(1, title.Number);
        var skip = Assert.Single(disc.Skipped);
        Assert.Equal(2, skip.Number);
        Assert.Equal("sua duração é de 3 segundos (mínimo 5 segs.)", skip.Reason);
    }

    [Fact]
    public void RecognizesDvdPaths()
    {
        SkipIfUnavailable();

        Assert.True(DvdScanner.LooksLikeDvd(dvd.Root));
        Assert.False(DvdScanner.LooksLikeDvd(_output));
    }

    [Fact]
    public async Task OpensFromIfoWithTracksAndLog()
    {
        SkipIfUnavailable();
        var opener = new SourceOpener(new FfprobeInspector());

        var contents = await opener.OpenAsync([VideoTsIfo], TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(SourceKind.Dvd, contents.Kind);
        Assert.Contains("Título #2 foi pulado pois sua duração é de 3 segundos (mínimo 5 segs.)", contents.Messages);
        Assert.Contains(contents.Messages, m => m.StartsWith("Título #1 foi adicionado (", StringComparison.Ordinal));

        var title = Assert.Single(contents.Titles);
        Assert.Equal("TESTE_DVD · Título 1", title.DisplayName);
        Assert.True(title.IsDisc);
        Assert.True(title.SizeBytes > 0);
        Assert.Equal(["-f", "dvdvideo", "-title", "1"], title.InputArguments);
        Assert.Equal(
            [(TrackType.Video, "und"), (TrackType.Audio, "por"), (TrackType.Audio, "eng"), (TrackType.Subtitle, "por")],
            title.Tracks.Select(t => (t.Type, t.Language)));
        Assert.True(title.Tracks[0].IsDefault);
        Assert.True(title.Tracks[1].IsDefault);
        Assert.False(title.Tracks[2].IsDefault);
    }

    [Fact]
    public async Task RipsTitleToMkvWithAllTracksAndChapters()
    {
        SkipIfUnavailable();
        var inspector = new FfprobeInspector();
        var contents = await new DvdReader(inspector).ReadAsync(dvd.Root, TimeSpan.Zero, TestContext.Current.CancellationToken);
        var title = contents.Titles[0];

        var job = MuxJob.FromSelection(title, _output);
        Assert.Equal(Path.Combine(_output, "TESTE_DVD_t01.mkv"), job.OutputPath);

        await new FfmpegMuxer().MuxAsync(job, cancellationToken: TestContext.Current.CancellationToken);

        var result = await inspector.InspectAsync(job.OutputPath, TestContext.Current.CancellationToken);
        Assert.Contains("matroska", result.FormatName);
        Assert.Equal(2, result.ChapterCount);
        Assert.InRange(result.Duration.TotalSeconds, 7.5, 8.5);
        Assert.Equal(
            [("mpeg2video", "und"), ("ac3", "por"), ("ac3", "eng"), ("dvd_subtitle", "por")],
            result.Tracks.Select(t => (t.Codec, t.Language)));
        Assert.True(result.Tracks[1].IsDefault);
        Assert.Equal(["0100E0", "0180BD", "0181BD", "0120BD"], result.Tracks.Select(t => t.SourceId));
        Assert.Equal("Mono", result.Tracks[1].Title);
    }

    [Fact]
    public async Task RipsTitleFromIso()
    {
        SkipIfUnavailable();
        Assert.SkipWhen(dvd.IsoPath is null, "genisoimage não encontrado");
        var inspector = new FfprobeInspector();
        var contents = await new DvdReader(inspector).ReadAsync(dvd.IsoPath!, TimeSpan.Zero, TestContext.Current.CancellationToken);

        var job = MuxJob.FromSelection(contents.Titles[1], _output);
        await new FfmpegMuxer().MuxAsync(job, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_output, "TESTE_ISO_t02.mkv"), job.OutputPath);
        var result = await inspector.InspectAsync(job.OutputPath, TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Tracks.Count);
    }
}
