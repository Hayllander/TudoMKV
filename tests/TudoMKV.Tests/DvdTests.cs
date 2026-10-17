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

    [Theory]
    [InlineData("TESTE_ISO", true)]
    [InlineData("DVD_VIDEO", true)]
    [InlineData("20110204_1906", false)]
    [InlineData("2011-02-04 19 06", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IgnoresVolumeLabelsThatAreOnlyNumbers(string? label, bool meaningful) =>
        Assert.Equal(meaningful, DvdScanner.IsMeaningfulLabel(label));

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
        Assert.Equal(["-f", "dvdvideo", "-title", "1", "-trim", "0"], title.InputArguments);
        Assert.Equal(
            [
                (TrackType.Video, "und"),
                (TrackType.Audio, "por"),
                (TrackType.Audio, "por"),
                (TrackType.Audio, "eng"),
                (TrackType.Audio, "eng"),
                (TrackType.Subtitle, "por"),
            ],
            title.Tracks.Select(t => (t.Type, t.Language)));
        Assert.True(title.Tracks[0].IsDefault);
        Assert.Equal([false, true, false, true], title.Tracks.Where(t => t.Type == TrackType.Audio).Select(t => t.IsDefault));
        Assert.False(title.Tracks[5].IsDefault);

        // Cada áudio mono ganha um estéreo 2.0 gerado na gravação, que assume a marca de faixa padrão.
        Assert.Equal([true, false, true, false], title.Tracks.Where(t => t.Type == TrackType.Audio).Select(t => t.IsGenerated));
        Assert.Equal([1, 2, 1, 2], title.Tracks.Where(t => t.Type == TrackType.Audio).Select(t => t.Channels));
        Assert.Equal("Stereo 2.0", title.Tracks[2].Title);
        Assert.Equal("por", title.Tracks[2].Language);
        Assert.Same(title.Tracks[1], title.Tracks[2].ConvertedFrom);
        Assert.Equal("0180BD", title.Tracks[1].SourceId);
        Assert.Null(title.Tracks[2].SourceId);
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
            [
                ("mpeg2video", "und"),
                ("ac3", "por"),
                ("ac3", "por"),
                ("ac3", "eng"),
                ("ac3", "eng"),
                ("dvd_subtitle", "por"),
            ],
            result.Tracks.Select(t => (t.Codec, t.Language)));
        Assert.Equal([false, true, false, true], result.Tracks.Where(t => t.Type == TrackType.Audio).Select(t => t.IsDefault));
        Assert.Equal([1, 2, 1, 2], result.Tracks.Where(t => t.Type == TrackType.Audio).Select(t => t.Channels));
        Assert.Equal(["0100E0", "0180BD", "0181BD", "0120BD"], result.Tracks.Where(t => t.SourceId is not null).Select(t => t.SourceId!));
        Assert.Equal(["Mono", "Stereo 2.0", "Mono", "Stereo 2.0"], result.Tracks.Where(t => t.Type == TrackType.Audio).Select(t => t.Title));
    }

    [Fact]
    public async Task HybridJobUsesPhysicalVobsForVideoAudioAndKeepsSubtitlesAndChapters()
    {
        SkipIfUnavailable();
        var inspector = new FfprobeInspector();
        var contents = await new DvdReader(inspector).ReadAsync(dvd.Root, TimeSpan.Zero, TestContext.Current.CancellationToken);
        var title = contents.Titles[0];

        var job = await new SelectionMuxJobBuilder(inspector).CreateAsync(title, _output, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(job.ExtraSources);
        Assert.Equal(1, job.ChaptersSourceIndex);
        Assert.Equal(["-fflags", "+genpts"], job.Source.InputArguments);
        Assert.Contains(job.Tracks, track => track.Type == TrackType.Video && track.InputIndex == 0);
        Assert.Contains(job.Tracks, track => track.Type == TrackType.Audio && track.InputIndex == 0);
        Assert.Contains(job.Tracks, track => track.Type == TrackType.Subtitle && track.InputIndex == 1);
        Assert.Contains(job.Tracks, track => track.IsGenerated && track.InputIndex == 0);

        try
        {
            await new FfmpegMuxer().MuxAsync(job, cancellationToken: TestContext.Current.CancellationToken);

            var result = await inspector.InspectAsync(job.OutputPath, TestContext.Current.CancellationToken);
            Assert.Equal(2, result.ChapterCount);
            Assert.Equal(
                [
                    ("mpeg2video", "und"),
                    ("ac3", "por"),
                    ("ac3", "por"),
                    ("ac3", "eng"),
                    ("ac3", "eng"),
                    ("dvd_subtitle", "por"),
                ],
                result.Tracks.Select(t => (t.Codec, t.Language)));
            Assert.Equal([1, 2, 1, 2], result.Tracks.Where(t => t.Type == TrackType.Audio).Select(t => t.Channels));
        }
        finally
        {
            foreach (var path in job.TemporaryFiles)
            {
                File.Delete(path);
            }
        }
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
        Assert.Equal(3, result.Tracks.Count);
    }

    [Fact]
    public async Task IsoWithFilesAtRootRipsOnWindowsOrExplainsElsewhere()
    {
        SkipIfUnavailable();
        Assert.SkipWhen(dvd.RootFilesIsoPath is null, "genisoimage não encontrado");

        if (!OperatingSystem.IsWindows())
        {
            var ex = Assert.Throws<InvalidDataException>(() => DvdScanner.Scan(dvd.RootFilesIsoPath!, TimeSpan.Zero));
            Assert.Contains("Extraia o conteúdo da ISO para uma pasta VIDEO_TS", ex.Message);
            return;
        }

        await RipFirstTitleAsync(dvd.RootFilesIsoPath!, "TESTE_RAIZ", expectedTitles: 2);
    }

    /// <summary>
    /// ISO real com os arquivos na raiz (ex.: TUDOMKV_TESTE_ISO_RAIZ="G:\Volcano High.iso"); grava o título mais curto.
    /// </summary>
    [Fact]
    public async Task RipsRealIsoWithFilesAtRoot()
    {
        var iso = Environment.GetEnvironmentVariable("TUDOMKV_TESTE_ISO_RAIZ");
        Assert.SkipWhen(string.IsNullOrEmpty(iso) || !File.Exists(iso), "defina TUDOMKV_TESTE_ISO_RAIZ com uma ISO real");
        Assert.SkipUnless(OperatingSystem.IsWindows(), "só no Windows");

        await RipFirstTitleAsync(iso!, label: null, expectedTitles: null);
    }

    private async Task RipFirstTitleAsync(string iso, string? label, int? expectedTitles)
    {
        var inspector = new FfprobeInspector();
        try
        {
            var contents = await new DvdReader(inspector).ReadAsync(iso, TimeSpan.Zero, TestContext.Current.CancellationToken);
            if (label is not null)
            {
                Assert.Equal(label, contents.Label);
            }

            if (expectedTitles is not null)
            {
                Assert.Equal(expectedTitles, contents.Titles.Count);
            }

            var title = contents.Titles.MinBy(t => t.Duration)!;
            var job = MuxJob.FromSelection(title, _output);
            await new FfmpegMuxer().MuxAsync(job, cancellationToken: TestContext.Current.CancellationToken);

            var result = await inspector.InspectAsync(job.OutputPath, TestContext.Current.CancellationToken);
            Assert.Equal(title.Tracks.Count, result.Tracks.Count);
            Assert.InRange(result.Duration.TotalSeconds, title.Duration.TotalSeconds - 2, title.Duration.TotalSeconds + 2);
        }
        finally
        {
            IsoMount.ReleaseAll();
        }
    }
}
