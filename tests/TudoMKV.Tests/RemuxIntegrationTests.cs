using System.Diagnostics;
using TudoMKV.Core;
using TudoMKV.Core.Models;
using TudoMKV.Muxing;

namespace TudoMKV.Tests;

/// <summary>Gera um MP4 de amostra com o ffmpeg real e remuxa para MKV. Pulado se não houver ffmpeg.</summary>
public sealed class RemuxIntegrationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tudomkv-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task RemuxesMp4WithSubtitlesAndChaptersToMkv()
    {
        var ffmpeg = ToolLocator.Find("ffmpeg");
        Assert.SkipWhen(ffmpeg is null || ToolLocator.Find("ffprobe") is null, "ffmpeg/ffprobe não encontrados");

        var input = await CreateSampleAsync(ffmpeg!);
        var inspector = new FfprobeInspector();

        var source = await inspector.InspectAsync(input, TestContext.Current.CancellationToken);
        Assert.Equal(2, source.ChapterCount);
        Assert.Contains(source.Tracks, t => t.Type == TrackType.Subtitle && t.Codec == "mov_text");

        var audio = source.Tracks.Single(t => t.Type == TrackType.Audio);
        audio.Language = "por";
        audio.Title = "Português";

        var job = MuxJob.FromSelection(source, Path.Combine(_directory, "saida"));
        var reports = new List<double>();
        await new FfmpegMuxer().MuxAsync(job, new SyncProgress(reports.Add), TestContext.Current.CancellationToken);

        Assert.True(File.Exists(job.OutputPath));
        Assert.Equal(1.0, reports.Last());

        var result = await inspector.InspectAsync(job.OutputPath, TestContext.Current.CancellationToken);
        Assert.Contains("matroska", result.FormatName);
        Assert.Equal(2, result.ChapterCount);
        Assert.Equal(3, result.Tracks.Count);
        Assert.Equal("subrip", result.Tracks.Single(t => t.Type == TrackType.Subtitle).Codec);

        var resultAudio = result.Tracks.Single(t => t.Type == TrackType.Audio);
        Assert.Equal("por", resultAudio.Language);
        Assert.Equal("Português", resultAudio.Title);
        Assert.InRange(result.Duration.TotalSeconds, 2.5, 3.5);
    }

    [Fact]
    public async Task FailedMuxReportsFfmpegLogAndLeavesNoFile()
    {
        var ffmpeg = ToolLocator.Find("ffmpeg");
        Assert.SkipWhen(ffmpeg is null, "ffmpeg não encontrado");

        var source = new MediaSource
        {
            Path = Path.Combine(_directory, "nao-existe.mp4"),
            Tracks = [new MediaTrack { Index = 0, Type = TrackType.Video, Codec = "h264" }],
        };
        var job = MuxJob.FromSelection(source);

        var error = await Assert.ThrowsAsync<MuxException>(() => new FfmpegMuxer().MuxAsync(job, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("nao-existe.mp4", error.Log);
        Assert.False(File.Exists(job.OutputPath));
    }

    private async Task<string> CreateSampleAsync(string ffmpeg)
    {
        var srt = Path.Combine(_directory, "legenda.srt");
        await File.WriteAllTextAsync(srt, "1\n00:00:00,500 --> 00:00:02,000\nOlá, TudoMKV!\n");

        var chapters = Path.Combine(_directory, "capitulos.txt");
        await File.WriteAllTextAsync(chapters, """
            ;FFMETADATA1
            [CHAPTER]
            TIMEBASE=1/1000
            START=0
            END=1500
            title=Início
            [CHAPTER]
            TIMEBASE=1/1000
            START=1500
            END=3000
            title=Fim

            """);

        var output = Path.Combine(_directory, "amostra.mp4");
        var startInfo = new ProcessStartInfo(ffmpeg) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[]
                 {
                     "-hide_banner", "-nostdin", "-y",
                     "-f", "lavfi", "-i", "testsrc=size=320x240:rate=25:duration=3",
                     "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
                     "-i", srt,
                     "-i", chapters,
                     "-map", "0", "-map", "1", "-map", "2", "-map_metadata", "3", "-map_chapters", "3",
                     "-c:v", "mpeg4", "-c:a", "aac", "-c:s", "mov_text",
                     output,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var log = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, log);
        return output;
    }

    /// <summary>IProgress síncrono, para não depender de SynchronizationContext no teste.</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
