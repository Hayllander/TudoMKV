using TudoMKV.Core;
using TudoMKV.Core.Models;
using TudoMKV.Muxing;

namespace TudoMKV.Tests;

public class FfmpegArgumentsTests
{
    private static MediaSource Source(params MediaTrack[] tracks) => new()
    {
        Path = Path.Combine("videos", "filme.mp4"),
        Duration = TimeSpan.FromMinutes(90),
        Tracks = tracks,
    };

    private static MediaTrack Track(int index, TrackType type, string codec) =>
        new() { Index = index, Type = type, Codec = codec };

    private static string ArgsText(MuxJob job) => string.Join(' ', FfmpegArguments.Build(job));

    [Fact]
    public void MapsOnlySelectedTracksAndCopiesStreams()
    {
        var audio = Track(1, TrackType.Audio, "aac");
        audio.Selected = false;
        var source = Source(Track(0, TrackType.Video, "h264"), audio, Track(2, TrackType.Audio, "ac3"));

        var args = FfmpegArguments.Build(MuxJob.FromSelection(source));
        var text = string.Join(' ', args);

        Assert.Contains("-map 0:0", text);
        Assert.Contains("-map 0:2", text);
        Assert.DoesNotContain("-map 0:1", text);
        Assert.Contains("-c copy", text);
        Assert.Equal(["-f", "matroska", Path.Combine("videos", "filme.mkv")], args.TakeLast(3));
    }

    [Fact]
    public void WritesLanguageTitleAndDispositionByOutputIndex()
    {
        var video = Track(0, TrackType.Video, "h264");
        var subtitle = Track(5, TrackType.Subtitle, "hdmv_pgs_subtitle");
        subtitle.Language = "POR";
        subtitle.Title = "Forçadas";
        subtitle.IsDefault = true;
        subtitle.IsForced = true;

        var text = ArgsText(MuxJob.FromSelection(Source(video, subtitle)));

        Assert.Contains("-metadata:s:1 language=por", text);
        Assert.Contains("-metadata:s:1 title=Forçadas", text);
        Assert.Contains("-disposition:1 default+forced", text);
        Assert.Contains("-disposition:0 0", text);
        Assert.Contains("-metadata:s:0 language=und", text);
    }

    [Fact]
    public void ConvertsMovTextToSrt()
    {
        var text = ArgsText(MuxJob.FromSelection(Source(
            Track(0, TrackType.Video, "h264"),
            Track(1, TrackType.Subtitle, "mov_text"))));

        Assert.Contains("-c:1 srt", text);
    }

    [Fact]
    public void AttachmentsGetNoMetadataOrDisposition()
    {
        var text = ArgsText(MuxJob.FromSelection(Source(
            Track(0, TrackType.Video, "h264"),
            Track(1, TrackType.Attachment, "ttf"))));

        Assert.Contains("-map 0:1", text);
        Assert.DoesNotContain("-metadata:s:1", text);
        Assert.DoesNotContain("-disposition:1", text);
    }

    [Fact]
    public void ChaptersCanBeDropped()
    {
        var source = Source(Track(0, TrackType.Video, "h264"));
        var keep = MuxJob.FromSelection(source);
        var drop = new MuxJob { Source = source, OutputPath = "x.mkv", Tracks = keep.Tracks, KeepChapters = false };

        Assert.Contains("-map_chapters 0", ArgsText(keep));
        Assert.Contains("-map_chapters -1", ArgsText(drop));
    }

    [Fact]
    public void RefusesEmptySelection()
    {
        var job = new MuxJob { Source = Source(), OutputPath = "x.mkv", Tracks = [] };

        Assert.Throws<ArgumentException>(() => FfmpegArguments.Build(job));
    }
}
