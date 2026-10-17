using System.Diagnostics;
using TudoMKV.Core;

namespace TudoMKV.Muxing;

/// <summary>Aplica tags globais Matroska em um MKV pronto usando o mkvpropedit.</summary>
public sealed class MkvTagWriter(string? mkvpropeditPath = null)
{
    private const int MaxLogLines = 40;

    /// <summary>
    /// Aplica as etiquetas globais do Matroska em um MKV pronto. O mkvpropedit só lê etiquetas de um arquivo,
    /// então elas passam por um temporário na pasta do sistema, apagado logo depois: a pasta do aplicativo
    /// (ou da instalação) nunca recebe um tags.xml para ficar por lá.
    /// </summary>
    public async Task ApplyGlobalTagsAsync(string mkvPath, string tagsXml, CancellationToken cancellationToken = default)
    {
        var toolPath = mkvpropeditPath ?? ToolLocator.Find("mkvpropedit");
        if (string.IsNullOrWhiteSpace(toolPath))
        {
            throw new FileNotFoundException("Não encontrei o mkvpropedit. Instale o MKVToolNix ou coloque o executável no PATH.");
        }

        var tagsPath = Path.Combine(Path.GetTempPath(), "TudoMKV", $"tags-{Guid.NewGuid():N}.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(tagsPath)!);
        await File.WriteAllTextAsync(tagsPath, tagsXml, cancellationToken);

        try
        {
            await RunAsync(toolPath, mkvPath, tagsPath, cancellationToken);
        }
        finally
        {
            TryDelete(tagsPath);
        }
    }

    private static async Task RunAsync(string toolPath, string mkvPath, string tagsPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(toolPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(mkvPath);
        startInfo.ArgumentList.Add("--tags");
        startInfo.ArgumentList.Add($"global:{tagsPath}");

        using var process = Process.Start(startInfo)
            ?? throw new MuxException("Não foi possível iniciar o mkvpropedit.");

        var logLines = new Queue<string>();
        var stdoutTask = ReadLinesAsync(process.StandardOutput, logLines);
        var stderrTask = ReadLinesAsync(process.StandardError, logLines);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        await Task.WhenAll(stdoutTask, stderrTask);

        if (process.ExitCode != 0)
        {
            throw new MuxException($"O mkvpropedit terminou com código {process.ExitCode}.", string.Join(Environment.NewLine, logLines));
        }
    }

    private static async Task ReadLinesAsync(StreamReader reader, Queue<string> lines)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (lines)
            {
                lines.Enqueue(line);
                if (lines.Count > MaxLogLines)
                {
                    lines.Dequeue();
                }
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>Um temporário que não pôde ser apagado não pode invalidar a gravação das etiquetas.</summary>
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
}
