using System.Diagnostics;
using TudoMKV.Muxing;

namespace TudoMKV.Tests;

/// <summary>
/// Monta um DVD-Video de teste com ffmpeg + dvdauthor + spumux (e uma ISO com genisoimage, se houver):
/// título 1 com 8 s, 2 capítulos, áudio em português e inglês e uma legenda em português;
/// título 2 com 3 s e um áudio em inglês. Fica indisponível se faltar alguma ferramenta.
/// </summary>
public sealed class TestDvd : IAsyncLifetime
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tudomkv-dvd-").FullName;

    /// <summary>Pasta que contém VIDEO_TS.</summary>
    public string Root => Path.Combine(_directory, "TESTE_DVD");

    public string? IsoPath { get; private set; }

    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var ffmpeg = ToolLocator.Find("ffmpeg");
        if (ffmpeg is null || ToolLocator.Find("ffprobe") is null)
        {
            SkipReason = "ffmpeg/ffprobe não encontrados";
            return;
        }

        var (_, demuxers) = await RunAsync(ffmpeg, ["-hide_banner", "-demuxers"]);
        if (!demuxers.Contains(" dvdvideo ", StringComparison.Ordinal))
        {
            SkipReason = "este ffmpeg não tem o demuxer dvdvideo";
            return;
        }

        var dvdauthor = ToolLocator.Find("dvdauthor");
        var spumux = ToolLocator.Find("spumux");
        if (dvdauthor is null || spumux is null)
        {
            SkipReason = "dvdauthor/spumux não encontrados";
            return;
        }

        await Require(ffmpeg,
        [
            "-hide_banner", "-y",
            "-f", "lavfi", "-i", "testsrc=size=720x576:rate=25:duration=8",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=8",
            "-f", "lavfi", "-i", "sine=frequency=880:duration=8",
            "-map", "0", "-map", "1", "-map", "2", "-target", "pal-dvd", "-aspect", "16:9", Local("t1.mpg"),
        ]);
        await Require(ffmpeg,
        [
            "-hide_banner", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=720x576:rate=25:duration=3",
            "-f", "lavfi", "-i", "sine=duration=3",
            "-target", "pal-dvd", Local("t2.mpg"),
        ]);

        // Legenda em imagem (2 cores), para não depender de fontes instaladas.
        await Require(ffmpeg,
        [
            "-hide_banner", "-y",
            "-f", "lavfi", "-i", "color=c=black@0.0:s=720x576,format=rgba,drawbox=x=200:y=480:w=320:h=40:color=white@1.0:t=fill",
            "-frames:v", "1", Local("sub.png"),
        ]);
        await File.WriteAllTextAsync(Local("sub.xml"),
            $"""<subpictures format="PAL"><stream><spu start="00:00:01.00" end="00:00:03.00" image="{Local("sub.png")}"/></stream></subpictures>""");
        await RunShellPipeAsync(spumux, ["-s", "0", Local("sub.xml")], Local("t1.mpg"), Local("t1s.mpg"));

        await File.WriteAllTextAsync(Local("dvd.xml"), $"""
            <dvdauthor dest="{Root}">
              <vmgm />
              <titleset>
                <titles>
                  <video format="pal" aspect="16:9" widescreen="nopanscan" />
                  <audio lang="pt" />
                  <audio lang="en" />
                  <subpicture lang="pt" />
                  <pgc><vob file="{Local("t1s.mpg")}" chapters="0,0:04" /></pgc>
                </titles>
              </titleset>
              <titleset>
                <titles>
                  <video format="pal" />
                  <audio lang="en" />
                  <pgc><vob file="{Local("t2.mpg")}" /></pgc>
                </titles>
              </titleset>
            </dvdauthor>
            """);
        await Require(dvdauthor, ["-x", Local("dvd.xml")], ("VIDEO_FORMAT", "PAL"));

        if (ToolLocator.Find("genisoimage") is { } genisoimage)
        {
            IsoPath = Local("teste.iso");
            await Require(genisoimage, ["-quiet", "-dvd-video", "-V", "TESTE_ISO", "-o", IsoPath, Root]);
        }
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    private string Local(string name) => Path.Combine(_directory, name);

    private static async Task Require(string tool, string[] arguments, params (string Name, string Value)[] environment)
    {
        var (exitCode, output) = await RunAsync(tool, arguments, environment);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"{Path.GetFileName(tool)} falhou:\n{output}");
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string tool, string[] arguments, params (string Name, string Value)[] environment)
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

        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout + await stderr);
    }

    /// <summary>spumux lê o MPEG da entrada padrão e escreve na saída padrão.</summary>
    private static async Task RunShellPipeAsync(string tool, string[] arguments, string input, string output)
    {
        var startInfo = new ProcessStartInfo(tool)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var stderr = process.StandardError.ReadToEndAsync();
        await using (var target = File.Create(output))
        {
            var copyOut = process.StandardOutput.BaseStream.CopyToAsync(target);
            await using (var source = File.OpenRead(input))
            {
                await source.CopyToAsync(process.StandardInput.BaseStream);
            }

            process.StandardInput.Close();
            await copyOut;
        }

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{Path.GetFileName(tool)} falhou:\n{await stderr}");
        }
    }
}
