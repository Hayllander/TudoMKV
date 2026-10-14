using TudoMKV.Core;
using TudoMKV.Core.Models;
using TudoMKV.Disc.Bluray;
using TudoMKV.Muxing;

namespace TudoMKV.Tests;

public sealed class BlurayTests(TestBluray bd) : IClassFixture<TestBluray>, IDisposable
{
    private readonly string _output = Directory.CreateTempSubdirectory("tudomkv-out-").FullName;

    public void Dispose() => Directory.Delete(_output, recursive: true);

    private void SkipIfUnavailable() => Assert.SkipWhen(bd.SkipReason is not null, bd.SkipReason ?? string.Empty);

    [Theory]
    [InlineData("BDMV/index.bdmv")]
    [InlineData("BDMV/MovieObject.bdmv")]
    [InlineData("BDMV")]
    [InlineData("")]
    public void AcceptsIndexMovieObjectOrFolders(string relative)
    {
        SkipIfUnavailable();
        var path = Path.Combine(bd.Root, relative.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(BlurayScanner.LooksLikeBluray(path));
        var disc = BlurayScanner.Scan(path, TimeSpan.Zero);

        Assert.Equal(Path.GetFullPath(bd.Root), disc.Root);
        Assert.Equal("TESTE BD", disc.Label);
        Assert.Equal(["00000", "00001"], disc.Titles.Select(t => t.Playlist));
    }

    [Fact]
    public void ReadsPlaylistDetails()
    {
        SkipIfUnavailable();

        var title = BlurayScanner.Scan(bd.Root, TimeSpan.Zero).Titles[0];

        Assert.InRange(title.Duration.TotalSeconds, 9.5, 10.5);
        Assert.Equal(2, title.Content.Chapters.Count);
        Assert.Equal(0, title.Content.Chapters[0], 2);
        Assert.Equal(5, title.Content.Chapters[1], 1);
        Assert.Equal("00000", Assert.Single(title.Content.Items).Clip);
        Assert.Equal(
            [(BlurayStreamKind.Video, "und"), (BlurayStreamKind.Audio, "por"), (BlurayStreamKind.Audio, "eng"), (BlurayStreamKind.Subtitle, "por")],
            title.Content.Streams.Select(s => (s.Kind, s.Language)));

        var clip = new FileInfo(title.ClipPaths[0]).Length;
        Assert.InRange(title.SizeBytes, clip * 0.9, clip * 1.0);
    }

    [Fact]
    public void SkipsShortPlaylistsAndSaysWhy()
    {
        SkipIfUnavailable();

        var disc = BlurayScanner.Scan(bd.Root, TimeSpan.FromSeconds(5));

        Assert.Equal("00000", Assert.Single(disc.Titles).Playlist);
        var skip = Assert.Single(disc.Skipped);
        Assert.Equal("00001.mpls", skip.Playlist);
        Assert.Equal("sua duração é de 3 segundos (mínimo 5 segs.)", skip.Reason);
    }

    [Fact]
    public async Task OpensIndexBdmvWithLanguagesAndLog()
    {
        SkipIfUnavailable();
        var opener = new SourceOpener(new FfprobeInspector());

        var contents = await opener.OpenAsync([Path.Combine(bd.Root, "BDMV", "index.bdmv")], TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(SourceKind.Bluray, contents.Kind);
        Assert.Contains("Título #00001.mpls foi pulado pois sua duração é de 3 segundos (mínimo 5 segs.)", contents.Messages);
        Assert.Contains("Arquivo 00000.m2ts foi adicionado ao título #1", contents.Messages);

        var title = Assert.Single(contents.Titles);
        Assert.Equal("TESTE BD · Título 1", title.DisplayName);
        Assert.Equal(2, title.ChapterCount);
        Assert.NotNull(title.ChaptersFile);
        Assert.Equal(
            [(TrackType.Video, "und"), (TrackType.Audio, "por"), (TrackType.Audio, "eng"), (TrackType.Subtitle, "por")],
            title.Tracks.Select(t => (t.Type, t.Language)));
        Assert.All(title.Tracks, t => Assert.True(t.Selected));
    }

    [Fact]
    public async Task RipsTitleToMkvWithLanguagesAndChapters()
    {
        SkipIfUnavailable();
        var inspector = new FfprobeInspector();
        var contents = await new BlurayReader(inspector).ReadAsync(bd.Root, TimeSpan.Zero, TestContext.Current.CancellationToken);

        var job = MuxJob.FromSelection(contents.Titles[0], _output);
        Assert.Equal(Path.Combine(_output, "TESTE BD_t01.mkv"), job.OutputPath);
        var reports = new List<double>();
        await new FfmpegMuxer().MuxAsync(job, new SyncProgress(reports.Add), TestContext.Current.CancellationToken);

        var result = await inspector.InspectAsync(job.OutputPath, TestContext.Current.CancellationToken);
        Assert.Contains("matroska", result.FormatName);
        Assert.Equal(2, result.ChapterCount);
        Assert.InRange(result.Duration.TotalSeconds, 9.5, 10.5);
        Assert.Equal(
            [("h264", "und"), ("ac3", "por"), ("ac3", "eng"), ("hdmv_pgs_subtitle", "por")],
            result.Tracks.Select(t => (t.Codec, t.Language)));
        Assert.Equal(1.0, reports.Last());
    }

    [Fact]
    public async Task RipsShortTitleThatFfmpegBlurayProtocolWouldRefuse()
    {
        SkipIfUnavailable();
        var inspector = new FfprobeInspector();
        var contents = await new BlurayReader(inspector).ReadAsync(bd.Root, TimeSpan.Zero, TestContext.Current.CancellationToken);

        var job = MuxJob.FromSelection(contents.Titles[1], _output);
        await new FfmpegMuxer().MuxAsync(job, cancellationToken: TestContext.Current.CancellationToken);

        var result = await inspector.InspectAsync(job.OutputPath, TestContext.Current.CancellationToken);
        Assert.Equal([("h264", "und"), ("ac3", "eng")], result.Tracks.Select(t => (t.Codec, t.Language)));
        Assert.InRange(result.Duration.TotalSeconds, 2.5, 3.5);
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
