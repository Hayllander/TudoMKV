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
    public void ReadsChaptersFromSeparateFileAndInputOptionsGoBeforeInput()
    {
        var source = new MediaSource
        {
            Path = "00800.ffconcat",
            InputArguments = ["-f", "concat", "-safe", "0"],
            ChaptersFile = "capitulos.txt",
            Tracks = [Track(0, TrackType.Video, "h264")],
        };

        var text = ArgsText(MuxJob.FromSelection(source, "saida"));

        Assert.Contains("-f concat -safe 0 -i 00800.ffconcat -i capitulos.txt", text);
        Assert.Contains("-map_chapters 1", text);
        Assert.Contains("-map 0:0", text);
    }

    [Fact]
    public void MapsTracksAndChaptersFromDifferentInputs()
    {
        var videoAudio = new MediaSource
        {
            Path = "merged.vob",
            Tracks =
            [
                new MediaTrack { Index = 0, Type = TrackType.Video, Codec = "mpeg2video" },
                new MediaTrack { Index = 1, Type = TrackType.Audio, Codec = "ac3" },
            ],
        };
        var dvd = new MediaSource
        {
            Path = "VIDEO_TS",
            InputArguments = ["-f", "dvdvideo", "-title", "1", "-trim", "0"],
            Tracks =
            [
                new MediaTrack { Index = 0, Type = TrackType.Video, Codec = "mpeg2video" },
                new MediaTrack { Index = 1, Type = TrackType.Audio, Codec = "ac3" },
                new MediaTrack { Index = 2, Type = TrackType.Subtitle, Codec = "dvd_subtitle" },
            ],
        };

        var job = new MuxJob
        {
            Source = videoAudio,
            ExtraSources = [dvd],
            OutputPath = "x.mkv",
            Tracks =
            [
                new MediaTrack { InputIndex = 0, Index = 0, Type = TrackType.Video, Codec = "mpeg2video" },
                new MediaTrack { InputIndex = 0, Index = 1, Type = TrackType.Audio, Codec = "ac3" },
                new MediaTrack { InputIndex = 1, Index = 2, Type = TrackType.Subtitle, Codec = "dvd_subtitle" },
            ],
            ChaptersSourceIndex = 1,
        };

        var text = ArgsText(job);

        Assert.Contains("-i merged.vob -f dvdvideo -title 1 -trim 0 -i VIDEO_TS", text);
        Assert.Contains("-map 0:0 -map 0:1 -map 1:2", text);
        Assert.Contains("-map_chapters 1", text);
    }

    [Fact]
    public void WritesAppNameDateAndSourceIdLikeMakeMkv()
    {
        var video = Track(0, TrackType.Video, "h264");
        video.SourceId = "001011";
        var date = new DateTimeOffset(2026, 10, 14, 11, 50, 2, TimeSpan.Zero);

        var text = string.Join(' ', FfmpegArguments.Build(MuxJob.FromSelection(Source(video, Track(1, TrackType.Audio, "ac3"))), date));

        Assert.Contains($"-metadata encoding_tool={FfmpegArguments.WritingApplication}", text);
        Assert.StartsWith("TudoMKV ", FfmpegArguments.WritingApplication);
        Assert.Contains("-metadata creation_time=2026-10-14T11:50:02Z", text);
        Assert.Contains("-metadata:s:0 SOURCE_ID=001011 -metadata:s:0 _STATISTICS_TAGS=SOURCE_ID", text);
        Assert.Contains("-metadata:s:0 _STATISTICS_WRITING_DATE_UTC=2026-10-14 11:50:02", text);
        Assert.DoesNotContain("-metadata:s:1 SOURCE_ID", text);
    }

    [Theory]
    [InlineData(SourceKind.Bluray, 0x1011, "001011")]
    [InlineData(SourceKind.Dvd, 0x1E0, "0100E0")]
    [InlineData(SourceKind.Dvd, 0x80, "0180BD")]
    [InlineData(SourceKind.Dvd, 0x20, "0120BD")]
    [InlineData(SourceKind.Files, 0x1011, null)]
    public void BuildsMakeMkvSourceIds(SourceKind kind, int streamId, string? expected)
    {
        Assert.Equal(expected, DiscTracks.SourceId(kind, streamId));
    }

    [Fact]
    public void RefusesEmptySelection()
    {
        var job = new MuxJob { Source = Source(), OutputPath = "x.mkv", Tracks = [] };

        Assert.Throws<ArgumentException>(() => FfmpegArguments.Build(job));
    }
}
