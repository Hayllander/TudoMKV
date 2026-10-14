using TudoMKV.Core.Models;
using TudoMKV.Muxing;

namespace TudoMKV.Tests;

public class FfprobeParseTests
{
    private const string Json = """
        {
          "streams": [
            { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 1920, "height": 1080,
              "disposition": { "default": 1, "forced": 0, "attached_pic": 0 }, "tags": { "language": "und" } },
            { "index": 1, "codec_name": "ac3", "codec_type": "audio", "channels": 6, "sample_rate": "48000",
              "disposition": { "default": 1, "forced": 0 }, "tags": { "language": "por", "title": "Dublado" } },
            { "index": 2, "codec_name": "mov_text", "codec_type": "subtitle",
              "disposition": { "default": 0, "forced": 1 }, "tags": { "language": "eng" } },
            { "index": 3, "codec_name": "mjpeg", "codec_type": "video", "width": 600, "height": 600,
              "disposition": { "default": 0, "attached_pic": 1 } },
            { "index": 4, "codec_type": "data", "tags": { "handler_name": "TimeCodeHandler" } }
          ],
          "chapters": [ { "id": 0 }, { "id": 1 } ],
          "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "5412.250000" }
        }
        """;

    [Fact]
    public void ParsesFormatDurationAndChapters()
    {
        var source = FfprobeInspector.Parse("/videos/filme.mp4", Json);

        Assert.Equal("filme.mp4", source.DisplayName);
        Assert.Equal(TimeSpan.FromSeconds(5412.25), source.Duration);
        Assert.Equal(2, source.ChapterCount);
        Assert.Equal(5, source.Tracks.Count);
    }

    [Fact]
    public void ParsesTrackDetails()
    {
        var tracks = FfprobeInspector.Parse("a.mp4", Json).Tracks;

        Assert.Equal(TrackType.Video, tracks[0].Type);
        Assert.Equal("h264 1920x1080", tracks[0].Summary);

        Assert.Equal(TrackType.Audio, tracks[1].Type);
        Assert.Equal("por", tracks[1].Language);
        Assert.Equal("Dublado", tracks[1].Title);
        Assert.Equal(6, tracks[1].Channels);
        Assert.Equal(48000, tracks[1].SampleRate);
        Assert.Equal("ac3 5.1", tracks[1].Summary);

        Assert.Equal(TrackType.Subtitle, tracks[2].Type);
        Assert.True(tracks[2].IsForced);
        Assert.False(tracks[2].IsDefault);
    }

    [Fact]
    public void CoverArtBecomesAttachmentAndDataIsNotSelected()
    {
        var tracks = FfprobeInspector.Parse("a.mp4", Json).Tracks;

        Assert.Equal(TrackType.Attachment, tracks[3].Type);
        Assert.Equal(TrackType.Data, tracks[4].Type);
        Assert.False(tracks[4].Selected);
        Assert.False(tracks[4].CanMux);
        Assert.Equal("desconhecido", tracks[4].Codec);
    }

    [Fact]
    public void MissingSectionsAreTolerated()
    {
        var source = FfprobeInspector.Parse("vazio.ts", "{}");

        Assert.Empty(source.Tracks);
        Assert.Equal(TimeSpan.Zero, source.Duration);
        Assert.Equal(0, source.ChapterCount);
    }
}
