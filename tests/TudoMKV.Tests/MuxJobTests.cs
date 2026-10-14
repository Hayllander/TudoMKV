using TudoMKV.Core;
using TudoMKV.Core.Models;
using TudoMKV.Muxing;

namespace TudoMKV.Tests;

public class MuxJobTests
{
    private static MediaSource Source(string path) => new()
    {
        Path = path,
        Tracks =
        [
            new MediaTrack { Index = 0, Type = TrackType.Video, Codec = "h264" },
            new MediaTrack { Index = 1, Type = TrackType.Data, Codec = "bin_data", Selected = true },
        ],
    };

    [Fact]
    public void OutputGoesNextToSourceByDefault()
    {
        var job = MuxJob.FromSelection(Source(Path.Combine("pasta", "filme.mp4")));

        Assert.Equal(Path.Combine("pasta", "filme.mkv"), job.OutputPath);
    }

    [Fact]
    public void OutputDirectoryIsRespected()
    {
        var job = MuxJob.FromSelection(Source(Path.Combine("pasta", "filme.mp4")), "saida");

        Assert.Equal(Path.Combine("saida", "filme.mkv"), job.OutputPath);
    }

    [Fact]
    public void RemuxingAnMkvDoesNotOverwriteIt()
    {
        var job = MuxJob.FromSelection(Source(Path.Combine("pasta", "filme.mkv")));

        Assert.Equal(Path.Combine("pasta", "filme.remux.mkv"), job.OutputPath);
    }

    [Fact]
    public void DataTracksAreNeverMuxed()
    {
        var job = MuxJob.FromSelection(Source("filme.mp4"));

        Assert.Equal([0], job.Tracks.Select(t => t.Index));
    }

    [Theory]
    [InlineData("out_time_us=30000000", 60_000_000, 0.5)]
    [InlineData("out_time_us=90000000", 60_000_000, 1.0)]
    [InlineData("out_time_us=-5", 60_000_000, 0.0)]
    public void ParsesFfmpegProgress(string line, double total, double expected)
    {
        Assert.True(FfmpegMuxer.TryParseProgress(line, total, out var fraction));
        Assert.Equal(expected, fraction, 3);
    }

    [Theory]
    [InlineData("frame=100", 60_000_000)]
    [InlineData("out_time_us=N/A", 60_000_000)]
    [InlineData("out_time_us=100", 0)]
    public void IgnoresOtherProgressLines(string line, double total)
    {
        Assert.False(FfmpegMuxer.TryParseProgress(line, total, out _));
    }
}
