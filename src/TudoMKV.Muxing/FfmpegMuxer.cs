using System.Diagnostics;
using System.Globalization;
using TudoMKV.Core;

namespace TudoMKV.Muxing;

/// <summary>Grava MKV chamando o ffmpeg como processo externo, com cópia de streams.</summary>
public sealed class FfmpegMuxer(string? ffmpegPath = null) : IMuxer
{
    private const int MaxLogLines = 40;

    public async Task MuxAsync(MuxJob job, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(ffmpegPath ?? ToolLocator.Require("ffmpeg"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in FfmpegArguments.Build(job))
        {
            startInfo.ArgumentList.Add(argument);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(job.OutputPath))!);

        using var process = Process.Start(startInfo)
            ?? throw new MuxException("Não foi possível iniciar o ffmpeg.");

        var errorLog = new Queue<string>();
        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(CancellationToken.None) is { } line)
            {
                lock (errorLog)
                {
                    errorLog.Enqueue(line);
                    if (errorLog.Count > MaxLogLines)
                    {
                        errorLog.Dequeue();
                    }
                }
            }
        }, CancellationToken.None);

        var totalMicroseconds = job.Source.Duration.Ticks / 10.0;

        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                if (TryParseProgress(line, totalMicroseconds, out var fraction))
                {
                    progress?.Report(fraction);
                }
            }

            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            TryDelete(job.OutputPath);
            throw;
        }

        await stderrTask;

        if (process.ExitCode != 0)
        {
            TryDelete(job.OutputPath);
            string log;
            lock (errorLog)
            {
                log = string.Join(Environment.NewLine, errorLog);
            }

            throw new MuxException($"O ffmpeg terminou com código {process.ExitCode}.", log);
        }

        progress?.Report(1.0);
    }

    /// <summary>Lê uma linha de <c>-progress pipe:1</c> e calcula a fração concluída.</summary>
    internal static bool TryParseProgress(string line, double totalMicroseconds, out double fraction)
    {
        fraction = 0;
        const string key = "out_time_us=";
        if (totalMicroseconds <= 0 || !line.StartsWith(key, StringComparison.Ordinal))
        {
            return false;
        }

        if (!long.TryParse(line.AsSpan(key.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
        {
            return false;
        }

        fraction = Math.Clamp(microseconds / totalMicroseconds, 0, 1);
        return true;
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

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
