using System.Diagnostics;
using TudoMKV.Muxing;

namespace TudoMKV.Tests;

/// <summary>
/// Monta um Blu-ray de teste com ffmpeg + tsMuxeR: playlist 00000 com 10 s, capítulos em 0 e 5 s, áudio em
/// português e inglês e uma legenda PGS em português; playlist 00001 com 3 s e áudio em inglês.
/// Fica indisponível se faltar alguma ferramenta.
/// </summary>
public sealed class TestBluray : IAsyncLifetime
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tudomkv-bd-").FullName;

    /// <summary>Pasta que contém BDMV.</summary>
    public string Root => Path.Combine(_directory, "TESTE BD");

    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var ffmpeg = ToolLocator.Find("ffmpeg");
        var tsMuxer = ToolLocator.Find("tsMuxeR");
        if (ffmpeg is null || ToolLocator.Find("ffprobe") is null || tsMuxer is null)
        {
            SkipReason = "ffmpeg/ffprobe/tsMuxeR não encontrados";
            return;
        }

        await Run(ffmpeg, ["-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc=size=1920x1080:rate=24:duration=10",
            "-c:v", "libx264", "-preset", "ultrafast", "-crf", "40", "-bf", "0", "-pix_fmt", "yuv420p", Local("v1.264")]);
        await Run(ffmpeg, ["-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc2=size=1920x1080:rate=24:duration=3",
            "-c:v", "libx264", "-preset", "ultrafast", "-crf", "40", "-bf", "0", "-pix_fmt", "yuv420p", Local("v2.264")]);
        await Run(ffmpeg, ["-hide_banner", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=10", "-ac", "2", "-ar", "48000", "-c:a", "ac3", Local("a1.ac3")]);
        await Run(ffmpeg, ["-hide_banner", "-y", "-f", "lavfi", "-i", "sine=frequency=880:duration=10", "-ac", "2", "-ar", "48000", "-c:a", "ac3", Local("a2.ac3")]);
        await Run(ffmpeg, ["-hide_banner", "-y", "-f", "lavfi", "-i", "sine=frequency=660:duration=3", "-ac", "2", "-ar", "48000", "-c:a", "ac3", Local("a3.ac3")]);
        await File.WriteAllTextAsync(Local("s.srt"), "1\n00:00:01,000 --> 00:00:03,000\nOlá Blu-ray\n");

        // tsMuxeR desenha a legenda PGS a partir do SRT com uma fonte do sistema.
        var font = new[] { ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "DejaVu Sans"), (@"C:\Windows\Fonts\arial.ttf", "Arial") }
            .FirstOrDefault(f => File.Exists(f.Item1));
        if (font == default)
        {
            SkipReason = "nenhuma fonte para gerar a legenda PGS";
            return;
        }

        var subtitle = $"""S_TEXT/UTF8, "{Local("s.srt")}", font-name="{font.Item2}", font-size=50, font-color=0xffffffff, bottom-offset=24, font-border=2, text-align=center, video-width=1920, video-height=1080, fps=24, lang=por""";

        await File.WriteAllTextAsync(Local("t1.meta"), $"""
            MUXOPT --blu-ray --custom-chapters=00:00:00.000;00:00:05.000 --vbr
            V_MPEG4/ISO/AVC, "{Local("v1.264")}", fps=24
            A_AC3, "{Local("a1.ac3")}", lang=por
            A_AC3, "{Local("a2.ac3")}", lang=eng
            {subtitle}

            """);
        await File.WriteAllTextAsync(Local("t2.meta"), $"""
            MUXOPT --blu-ray --vbr --mplsOffset=1 --m2tsOffset=1
            V_MPEG4/ISO/AVC, "{Local("v2.264")}", fps=24
            A_AC3, "{Local("a3.ac3")}", lang=eng

            """);
        await Run(tsMuxer, [Local("t1.meta"), Root]);
        await Run(tsMuxer, [Local("t2.meta"), Root]);
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    private string Local(string name) => Path.Combine(_directory, name);

    private static async Task Run(string tool, string[] arguments)
    {
        var startInfo = new ProcessStartInfo(tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{Path.GetFileName(tool)} falhou:\n{await stdout}{await stderr}");
        }
    }
}
