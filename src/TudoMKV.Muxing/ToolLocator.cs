namespace TudoMKV.Muxing;

/// <summary>Encontra executáveis externos (ffmpeg, ffprobe) ao lado do app ou no PATH.</summary>
public static class ToolLocator
{
    /// <summary>Variável de ambiente opcional com a pasta onde estão ffmpeg e ffprobe.</summary>
    public const string FfmpegDirVariable = "TUDOMKV_FFMPEG_DIR";

    public static string? Find(string tool)
    {
        var fileName = OperatingSystem.IsWindows() ? tool + ".exe" : tool;

        foreach (var directory in CandidateDirectories())
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string Require(string tool) =>
        Find(tool) ?? throw new FileNotFoundException(
            $"Não encontrei o {tool}. Coloque-o na pasta runtimes/ffmpeg ao lado do TudoMKV, " +
            $"aponte {FfmpegDirVariable} para a pasta dele, ou instale-o no PATH.");

    private static IEnumerable<string> CandidateDirectories()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(FfmpegDirVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            yield return fromEnvironment;
        }

        yield return Path.Combine(AppContext.BaseDirectory, "runtimes", "ffmpeg");
        yield return AppContext.BaseDirectory;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return directory;
        }
    }
}
