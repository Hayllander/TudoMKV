using System.Runtime.InteropServices;
using System.Text.Json;

namespace TudoMKV.App.Integration;

/// <summary>
/// Identifica a versão em uso, como o MakeMKV faz na primeira linha do log, e consulta o GitHub para
/// avisar quando houver uma versão mais nova publicada. Sem internet a consulta falha em silêncio.
/// </summary>
public static class UpdateChecker
{
    /// <summary>Página de onde as versões são baixadas.</summary>
    public const string ReleasesPage = "https://github.com/Hayllander/TudoMKV";

    private const string LatestReleaseApi = "https://api.github.com/repos/Hayllander/TudoMKV/releases/latest";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Versão de três números, a mesma que o "Sobre" mostra.</summary>
    public static string CurrentVersion { get; } =
        typeof(UpdateChecker).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>A linha de abertura do log: "TudoMKV v0.5.1 win(x64-release) Iniciado".</summary>
    public static string StartupLine() => $"TudoMKV v{CurrentVersion} {Platform()} Iniciado";

    /// <summary>
    /// Devolve o aviso quando o GitHub tem uma versão mais nova que a em uso. Sem versão nova, sem release
    /// publicada ou sem internet, devolve nulo — o aplicativo abre igual, apenas sem o aviso.
    /// </summary>
    public static async Task<string?> NewerVersionLineAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.UserAgent.ParseAdd($"TudoMKV/{CurrentVersion}");

            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!json.RootElement.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var latest = tag.GetString()?.TrimStart('v', 'V') ?? string.Empty;
            return Version.TryParse(latest, out var parsed) && parsed > new Version(CurrentVersion)
                ? $"versão {latest} disponível para download em {ReleasesPage}"
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>"win(x64-release)": sistema, arquitetura e tipo de build, no formato do MakeMKV.</summary>
    private static string Platform()
    {
        var system = OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsMacOS() ? "osx"
            : OperatingSystem.IsLinux() ? "linux"
            : "unknown";

        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "unknown",
        };

#if DEBUG
        const string build = "debug";
#else
        const string build = "release";
#endif
        return $"{system}({architecture}-{build})";
    }
}
