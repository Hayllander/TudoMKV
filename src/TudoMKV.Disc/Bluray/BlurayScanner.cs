using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace TudoMKV.Disc.Bluray;

/// <summary>Um título do Blu-ray: uma playlist, com caminho completo de cada clipe e tamanho estimado.</summary>
public sealed record BlurayTitle(string Playlist, Playlist Content, IReadOnlyList<string> ClipPaths, long SizeBytes)
{
    public TimeSpan Duration => TimeSpan.FromSeconds(Content.Duration);
}

/// <summary>Uma playlist que não virou título, e por quê (para o log).</summary>
public sealed record BluraySkip(string Playlist, string Reason);

public sealed record BlurayDisc(string Label, string Root, IReadOnlyList<BlurayTitle> Titles, IReadOnlyList<BluraySkip> Skipped);

/// <summary>
/// Lista os títulos de um Blu-ray a partir da pasta (ou do index.bdmv / MovieObject.bdmv), lendo as playlists
/// em C#. Como o MakeMKV, pula playlists curtas e as que repetem outra.
/// </summary>
public static partial class BlurayScanner
{
    public static bool LooksLikeBluray(string path) => FindRoot(path) is not null;

    public static BlurayDisc Scan(string path, TimeSpan minimumLength)
    {
        var root = FindRoot(path) ?? throw new DirectoryNotFoundException($"Não encontrei a pasta BDMV em {path}.");
        var bdmv = Child(root, "BDMV")!;
        var playlistDir = Child(bdmv, "PLAYLIST") ?? throw new DirectoryNotFoundException("BDMV/PLAYLIST não encontrado.");
        var streamDir = Child(bdmv, "STREAM") ?? throw new DirectoryNotFoundException("BDMV/STREAM não encontrado.");
        var clipInfoDir = Child(bdmv, "CLIPINF");

        var titles = new List<BlurayTitle>();
        var skipped = new List<BluraySkip>();
        var seen = new HashSet<string>();

        foreach (var file in Directory.EnumerateFiles(playlistDir).Where(f => f.EndsWith(".mpls", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            Playlist playlist;
            try
            {
                playlist = MplsParser.Parse(File.ReadAllBytes(file));
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or IndexOutOfRangeException)
            {
                skipped.Add(new BluraySkip(name, "não pôde ser lido"));
                continue;
            }

            if (playlist.Items.Count == 0 || playlist.Duration < minimumLength.TotalSeconds)
            {
                skipped.Add(new BluraySkip(name, $"sua duração é de {(int)playlist.Duration} segundos (mínimo {(int)minimumLength.TotalSeconds} segs.)"));
                continue;
            }

            // Playlists idênticas (mesmos clipes e trechos) aparecem uma vez só.
            var signature = string.Join('|', playlist.Items.Select(i => $"{i.Clip}:{i.In:F2}-{i.Out:F2}"));
            if (!seen.Add(signature))
            {
                skipped.Add(new BluraySkip(name, "é uma cópia de outro título"));
                continue;
            }

            var clips = playlist.Items.Select(i => FindFile(streamDir, i.Clip + ".m2ts")).ToList();
            if (clips.Any(c => c is null))
            {
                skipped.Add(new BluraySkip(name, "aponta para um arquivo .m2ts que não existe"));
                continue;
            }

            var size = playlist.Items.Zip(clips, (item, clip) => EstimateSize(item, clip!, clipInfoDir)).Sum();
            titles.Add(new BlurayTitle(Path.GetFileNameWithoutExtension(name), playlist, clips!, size));
        }

        return new BlurayDisc(ReadLabel(root, bdmv), root, titles, skipped);
    }

    /// <summary>Aceita a raiz do disco, a pasta BDMV, ou index.bdmv / MovieObject.bdmv dentro dela.</summary>
    private static string? FindRoot(string path)
    {
        if (File.Exists(path))
        {
            var fileName = Path.GetFileName(path);
            if (!fileName.Equals("index.bdmv", StringComparison.OrdinalIgnoreCase)
                && !fileName.Equals("MovieObject.bdmv", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            path = Path.GetDirectoryName(Path.GetFullPath(path))!;
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Path.GetFileName(trimmed).Equals("BACKUP", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = Path.GetDirectoryName(trimmed)!;
        }

        if (Path.GetFileName(trimmed).Equals("BDMV", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = Path.GetDirectoryName(trimmed)!;
        }

        var bdmv = Child(trimmed, "BDMV");
        return bdmv is not null && Child(bdmv, "PLAYLIST") is not null ? trimmed : null;
    }

    /// <summary>Nome do disco: BDMV/META/DL/bdmt_*.xml (como o MakeMKV mostra), senão o nome da pasta ou do drive.</summary>
    private static string ReadLabel(string root, string bdmv)
    {
        if (Child(bdmv, "META") is { } meta && Child(meta, "DL") is { } dl)
        {
            var files = Directory.EnumerateFiles(dl, "bdmt_*.xml").ToList();
            var preferred = files.OrderBy(f => Path.GetFileName(f).ToLowerInvariant() switch
            {
                "bdmt_por.xml" => 0,
                "bdmt_eng.xml" => 1,
                _ => 2,
            });
            foreach (var file in preferred)
            {
                var match = DiscNameRegex().Match(File.ReadAllText(file));
                if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                {
                    return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
                }
            }
        }

        return DriveLabel(root) ?? new DirectoryInfo(root).Name;
    }

    internal static string? DriveLabel(string root)
    {
        try
        {
            var full = Path.GetFullPath(root);
            if (!string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var drive = new DriveInfo(full);
            return drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.VolumeLabel : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Tamanho do trecho tocado: proporcional à duração do clipe, que vem do .clpi.</summary>
    private static long EstimateSize(PlayItem item, string clipPath, string? clipInfoDir)
    {
        var fileSize = new FileInfo(clipPath).Length;
        if (clipInfoDir is null || FindFile(clipInfoDir, item.Clip + ".clpi") is not { } clpi)
        {
            return fileSize;
        }

        var clipDuration = ClipDuration(File.ReadAllBytes(clpi));
        return clipDuration is > 0
            ? (long)(fileSize * Math.Clamp(item.Duration / clipDuration.Value, 0, 1))
            : fileSize;
    }

    /// <summary>Duração do clipe pela SequenceInfo do .clpi (início do primeiro STC ao fim do último).</summary>
    internal static double? ClipDuration(byte[] clpi)
    {
        try
        {
            if (Encoding.ASCII.GetString(clpi, 0, 4) != "HDMV")
            {
                return null;
            }

            var sequence = (int)BinaryPrimitives.ReadUInt32BigEndian(clpi.AsSpan(8));
            var atcCount = clpi[sequence + 5];
            var position = sequence + 6;
            uint? start = null;
            uint end = 0;
            for (var a = 0; a < atcCount; a++)
            {
                var stcCount = clpi[position + 4];
                position += 6;
                for (var s = 0; s < stcCount; s++)
                {
                    start ??= BinaryPrimitives.ReadUInt32BigEndian(clpi.AsSpan(position + 6));
                    end = BinaryPrimitives.ReadUInt32BigEndian(clpi.AsSpan(position + 10));
                    position += 14;
                }
            }

            return start is null || end <= start ? null : (end - start.Value) / 45000.0;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static string? Child(string directory, string name) =>
        Directory.EnumerateDirectories(directory)
            .FirstOrDefault(d => Path.GetFileName(d).Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string? FindFile(string directory, string name) =>
        Directory.EnumerateFiles(directory)
            .FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"<di:name>([^<]*)</di:name>", RegexOptions.IgnoreCase)]
    private static partial Regex DiscNameRegex();
}
