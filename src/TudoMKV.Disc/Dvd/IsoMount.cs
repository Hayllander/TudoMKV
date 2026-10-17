using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace TudoMKV.Disc.Dvd;

/// <summary>
/// Para ISOs com os arquivos do DVD na raiz (sem pasta VIDEO_TS e sem UDF), que o demuxer dvdvideo do ffmpeg
/// não abre: monta a ISO no Windows sem letra de unidade e cria uma junção numa pasta temporária apontando
/// para o volume. Numa pasta comum a libdvdread procura os IFO/VOB também na raiz, então o ffmpeg abre a junção.
/// Nada é copiado. A montagem vale até <see cref="ReleaseAll"/> (nova origem aberta ou fim do programa).
/// </summary>
public static class IsoMount
{
    private static readonly Lock Sync = new();
    private static readonly Dictionary<string, Mount> Mounts = new(StringComparer.OrdinalIgnoreCase);

    // Monta (se ainda não estiver montada), espera o volume aparecer e cria a junção para ele.
    private const string MountScript = """
        $ErrorActionPreference = 'Stop'
        $image = Get-DiskImage -ImagePath $env:TUDOMKV_ISO
        $mounted = $false
        if (-not $image.Attached) {
            $image = Mount-DiskImage -ImagePath $env:TUDOMKV_ISO -NoDriveLetter -Access ReadOnly -PassThru
            $mounted = $true
        }
        $volume = $null
        for ($i = 0; $i -lt 50 -and -not $volume; $i++) {
            $volume = $image | Get-Volume -ErrorAction SilentlyContinue
            if (-not $volume) { Start-Sleep -Milliseconds 100 }
        }
        if (-not $volume) { throw 'o volume da ISO não apareceu' }
        cmd /c mklink /J "$env:TUDOMKV_LINK" "$($volume.Path)" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'não foi possível criar a junção' }
        "$mounted"
        """;

    private const string DismountScript = "Dismount-DiskImage -ImagePath $env:TUDOMKV_ISO | Out-Null";

    /// <summary>Devolve uma pasta com os arquivos da raiz da ISO, montando-a se preciso.</summary>
    /// <exception cref="InvalidOperationException">A ISO não pôde ser montada.</exception>
    [SupportedOSPlatform("windows")]
    public static string Open(string isoPath)
    {
        isoPath = Path.GetFullPath(isoPath);
        lock (Sync)
        {
            if (Mounts.TryGetValue(isoPath, out var existing) && Directory.Exists(existing.Link))
            {
                return existing.Link;
            }

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(isoPath.ToUpperInvariant())))[..16];
            var link = Path.Combine(Path.GetTempPath(), "TudoMKV", "iso-" + hash);
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            RemoveLink(link); // sobra de uma execução interrompida

            var (exitCode, output) = RunPowerShell(MountScript, isoPath, link);
            if (exitCode != 0 || !Directory.Exists(link))
            {
                throw new InvalidOperationException($"não foi possível montar a ISO ({FirstLine(output)})");
            }

            var weMounted = output.Contains("True", StringComparison.OrdinalIgnoreCase);
            Mounts[isoPath] = new Mount(link, weMounted);
            return link;
        }
    }

    /// <summary>Remove as junções e desmonta as ISOs que o TudoMKV montou (nada a fazer fora do Windows).</summary>
    public static void ReleaseAll()
    {
        lock (Sync)
        {
            foreach (var (isoPath, mount) in Mounts)
            {
                RemoveLink(mount.Link);
                if (mount.WeMounted)
                {
                    RunPowerShell(DismountScript, isoPath, mount.Link);
                }
            }

            Mounts.Clear();
        }
    }

    private static void RemoveLink(string link)
    {
        try
        {
            // Sem "recursive": remove só a junção, nunca o conteúdo da ISO.
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static (int ExitCode, string Output) RunPowerShell(string script, string isoPath, string link)
    {
        // Caminhos vão por variável de ambiente, para aspas e espaços no nome não quebrarem o comando.
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        startInfo.Environment["TUDOMKV_ISO"] = isoPath;
        startInfo.Environment["TUDOMKV_LINK"] = link;

        try
        {
            using var process = Process.Start(startInfo)!;
            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdout + stderr.Result);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    private static string FirstLine(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "sem detalhes";

    private sealed record Mount(string Link, bool WeMounted);
}
