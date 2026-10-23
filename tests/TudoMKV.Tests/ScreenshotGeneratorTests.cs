using System.Diagnostics;
using TudoMKV.Muxing;

namespace TudoMKV.Tests;

public class ScreenshotGeneratorTests
{
    [Fact]
    public void SpreadsFifteenShotsBetweenFiveAndNinetyFivePercentLikeTheScript()
    {
        // 100 s: de 5 s a 95 s, passo de 9000/16 = 562 centésimos.
        var times = ScreenshotGenerator.Timestamps(TimeSpan.FromSeconds(100));

        Assert.Equal(15, times.Count);
        Assert.Equal("00:00:10.62", ScreenshotGenerator.FormatTime(times[0]));
        Assert.Equal("00:01:29.30", ScreenshotGenerator.FormatTime(times[14]));
    }

    [Theory]
    [InlineData(720, 480, "4:3", "scale=720:540,setsar=1")]
    [InlineData(720, 480, "16:9", "scale=853:480,setsar=1")]
    [InlineData(720, 576, "4:3", "scale=768:576,setsar=1")]
    [InlineData(720, 576, "16:9", "scale=1024:576,setsar=1")]
    [InlineData(1920, 1080, "16:9", "scale=round(ih*dar):ih,setsar=1")]
    [InlineData(1920, 1080, null, "setsar=1")]
    [InlineData(1920, 1080, "N/A", "setsar=1")]
    [InlineData(1920, 1080, "0:1", "setsar=1")]
    public void ShowsTheFrameAtItsDisplaySize(int width, int height, string? dar, string filter) =>
        Assert.Equal(filter, ScreenshotGenerator.DisplayFilter(width, height, dar));

    [Fact]
    public void ReadsSizeAspectAndDurationFromFfprobe()
    {
        const string json = """
            { "streams": [ { "width": 720, "height": 480, "sample_aspect_ratio": "32:27", "display_aspect_ratio": "16:9" } ],
              "format": { "duration": "3995.123000" } }
            """;

        var video = ScreenshotGenerator.ParseProbe(json);

        Assert.NotNull(video);
        Assert.Equal((720, 480, "16:9"), (video.Width, video.Height, video.DisplayAspectRatio));
        Assert.Equal(TimeSpan.FromSeconds(3995.123), video.Duration);
        Assert.Equal("Screenshots: 720x480 · SAR 32:27 · DAR 16:9", ScreenshotGenerator.Summary(video));
    }

    [Fact]
    public async Task GeneratesTheShotsNextToTheMkv()
    {
        var ffmpeg = ToolLocator.Find("ffmpeg");
        Assert.SkipWhen(ffmpeg is null || ToolLocator.Find("ffprobe") is null, "ffmpeg/ffprobe não encontrados");

        var folder = Directory.CreateTempSubdirectory("tudomkv-shots-");
        try
        {
            var mkv = Path.Combine(folder.FullName, "Filme.mkv");
            using (var process = Process.Start(new ProcessStartInfo(ffmpeg!)
            {
                ArgumentList =
                {
                    "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=size=720x480:rate=25:duration=20",
                    "-aspect", "16:9", "-c:v", "mpeg2video", mkv,
                },
                UseShellExecute = false,
                CreateNoWindow = true,
            })!)
            {
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            }

            File.WriteAllText(Path.Combine(folder.FullName, "Filme_screenshot_099.png"), "antiga");

            var created = await new ScreenshotGenerator().GenerateAsync(mkv, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(15, created);
            var shots = Directory.GetFiles(folder.FullName, "Filme_screenshot_*.png").Select(Path.GetFileName).Order().ToList();
            Assert.Equal(Enumerable.Range(1, 15).Select(i => $"Filme_screenshot_{i:000}.png"), shots);
            using var image = File.OpenRead(Path.Combine(folder.FullName, "Filme_screenshot_001.png"));
            var header = new byte[24];
            await image.ReadExactlyAsync(header, TestContext.Current.CancellationToken);
            var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            Assert.Equal(853, width);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
