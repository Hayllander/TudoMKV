using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using TudoMKV.Core;

namespace TudoMKV.Muxing;

/// <summary>
/// Gera as capturas de tela de um MKV pronto, como o ScreenShot.cmd: 15 PNGs espalhados entre 5% e 95% da
/// duração, ao lado do arquivo, com nome <c>Base_screenshot_001.png</c>. A imagem sai no formato de exibição
/// (o DVD anamórfico 720x480 16:9 vira 853x480) e a busca é a rápida, com a precisa de reserva quando a rápida
/// não produz o quadro.
/// </summary>
public sealed class ScreenshotGenerator(string? ffmpegPath = null, string? ffprobePath = null)
{
    public const int Count = 15;
    private const int StartPercent = 5;
    private const int EndPercent = 95;

    /// <summary>Gera as capturas e devolve quantas saíram; as que falham são informadas pelo <paramref name="log"/>.</summary>
    public async Task<int> GenerateAsync(string videoPath, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        var video = await ProbeAsync(videoPath, cancellationToken);
        var filter = DisplayFilter(video.Width, video.Height, video.DisplayAspectRatio);
        log?.Invoke(Summary(video));
        var directory = Path.GetDirectoryName(Path.GetFullPath(videoPath))!;
        var baseName = Path.GetFileNameWithoutExtension(videoPath);

        // Capturas de uma gravação anterior do mesmo arquivo não podem se misturar às novas.
        foreach (var old in Directory.EnumerateFiles(directory, $"{baseName}_screenshot_*.png"))
        {
            TryDelete(old);
        }

        var created = 0;
        var times = Timestamps(video.Duration);
        for (var i = 0; i < times.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = Path.Combine(directory, $"{baseName}_screenshot_{i + 1:000}.png");
            var time = FormatTime(times[i]);

            await CaptureAsync(videoPath, time, filter, output, fastSeek: true, cancellationToken);
            if (!File.Exists(output))
            {
                await CaptureAsync(videoPath, time, filter, output, fastSeek: false, cancellationToken);
            }

            if (File.Exists(output))
            {
                created++;
            }
            else
            {
                log?.Invoke($"Screenshot {i + 1}/{times.Count} falhou em {time}");
            }
        }

        return created;
    }

    /// <summary>
    /// Os instantes das capturas, com as contas do script em centésimos: o trecho de 5% a 95% é dividido em
    /// <see cref="Count"/> + 1 partes e cada captura cai numa divisa, sem pegar as pontas.
    /// </summary>
    internal static IReadOnlyList<TimeSpan> Timestamps(TimeSpan duration)
    {
        var durationCs = (long)Math.Round(duration.TotalSeconds * 100);
        var startCs = durationCs * StartPercent / 100;
        var endCs = durationCs * EndPercent / 100;
        var stepCs = (endCs - startCs) / (Count + 1);
        if (stepCs <= 0)
        {
            stepCs = 6000;
        }

        return Enumerable.Range(1, Count)
            .Select(i => TimeSpan.FromMilliseconds((startCs + stepCs * i) * 10))
            .ToList();
    }

    /// <summary>
    /// Filtro que deixa o quadro com pixels quadrados no tamanho de exibição. Os DVDs 720x480 e 720x576 4:3 e
    /// 16:9 usam os tamanhos fixos do script; o resto escala a largura pela proporção de exibição.
    /// </summary>
    internal static string DisplayFilter(int width, int height, string? displayAspectRatio)
    {
        if (string.IsNullOrWhiteSpace(displayAspectRatio)
            || displayAspectRatio.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            || displayAspectRatio.StartsWith("0:", StringComparison.Ordinal))
        {
            return "setsar=1";
        }

        var fixedSize = (width, height, displayAspectRatio) switch
        {
            (720, 480, "4:3") => "720:540",
            (720, 480, "16:9") => "853:480",
            (720, 576, "4:3") => "768:576",
            (720, 576, "16:9") => "1024:576",
            _ => null,
        };

        return $"scale={fixedSize ?? "round(ih*dar):ih"},setsar=1";
    }

    /// <summary>O cabeçalho do script, numa linha: "Screenshots: 720x480 · SAR 32:27 · DAR 16:9".</summary>
    internal static string Summary(VideoInfo video) =>
        $"Screenshots: {video.Width}x{video.Height} · SAR {video.SampleAspectRatio ?? "N/A"} · DAR {video.DisplayAspectRatio ?? "N/A"}";

    internal static string FormatTime(TimeSpan time) =>
        time.ToString(@"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture);

    private async Task CaptureAsync(
        string videoPath, string time, string filter, string output, bool fastSeek, CancellationToken cancellationToken)
    {
        // Busca rápida: -ss antes do -i salta direto ao quadro-chave; a precisa decodifica até o instante.
        string[] input = fastSeek ? ["-ss", time, "-i", videoPath] : ["-i", videoPath, "-ss", time];
        await RunAsync(
            ffmpegPath ?? ToolLocator.Require("ffmpeg"),
            ["-hide_banner", "-loglevel", "error", "-y", .. input, "-an", "-sn", "-dn", "-frames:v", "1", "-vf", filter, output],
            cancellationToken);
    }

    private async Task<VideoInfo> ProbeAsync(string videoPath, CancellationToken cancellationToken)
    {
        var json = await RunAsync(
            ffprobePath ?? ToolLocator.Require("ffprobe"),
            [
                "-v", "error", "-select_streams", "v:0",
                "-show_entries", "stream=width,height,sample_aspect_ratio,display_aspect_ratio:format=duration",
                "-of", "json", videoPath,
            ],
            cancellationToken);

        return ParseProbe(json)
            ?? throw new MuxException($"Não consegui ler o vídeo de {Path.GetFileName(videoPath)} para as screenshots.");
    }

    internal static VideoInfo? ParseProbe(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
        {
            return null;
        }

        var stream = streams[0];
        if (!stream.TryGetProperty("width", out var width) || !stream.TryGetProperty("height", out var height)
            || !root.TryGetProperty("format", out var format) || !format.TryGetProperty("duration", out var duration)
            || !double.TryParse(duration.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        var sar = stream.TryGetProperty("sample_aspect_ratio", out var sample) ? sample.GetString() : null;
        var dar = stream.TryGetProperty("display_aspect_ratio", out var ratio) ? ratio.GetString() : null;
        return new VideoInfo(width.GetInt32(), height.GetInt32(), sar, dar, TimeSpan.FromSeconds(seconds));
    }

    private static async Task<string> RunAsync(string tool, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new MuxException($"Não foi possível iniciar o {Path.GetFileNameWithoutExtension(tool)}.");
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await stderr;
            return stdout;
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal sealed record VideoInfo(int Width, int Height, string? SampleAspectRatio, string? DisplayAspectRatio, TimeSpan Duration);
}
