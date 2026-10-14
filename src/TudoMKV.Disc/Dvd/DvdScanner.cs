namespace TudoMKV.Disc.Dvd;

public sealed record DvdTitle(int Number, TimeSpan Duration, int Chapters, int Angles, int TitleSet, int Pgc);

/// <summary>Resultado da leitura dos IFO: rótulo, caminho que o ffmpeg deve abrir e títulos.</summary>
public sealed record DvdDisc(string Label, string InputPath, IReadOnlyList<DvdTitle> Titles);

/// <summary>Lista os títulos de um DVD a partir de um drive, de uma pasta (com ou sem VIDEO_TS no caminho) ou de uma ISO.</summary>
public static class DvdScanner
{
    public static bool LooksLikeDvd(string path) =>
        IsIso(path) || (Directory.Exists(path) && FindVideoTs(path) is not null);

    public static DvdDisc Scan(string path)
    {
        var (source, inputPath, fallbackLabel) = Open(path);
        using (source)
        {
            var vmg = source.ReadVideoTsFile("VIDEO_TS.IFO")
                ?? throw new InvalidDataException("VIDEO_TS.IFO não encontrado.");

            var titles = new List<DvdTitle>();
            var seen = new HashSet<(int TitleSet, int Pgc)>();
            var titleSets = new Dictionary<int, byte[]?>();

            foreach (var title in IfoParser.ParseVmgTitles(vmg))
            {
                if (!titleSets.TryGetValue(title.TitleSet, out var vts))
                {
                    vts = source.ReadVideoTsFile($"VTS_{title.TitleSet:00}_0.IFO");
                    titleSets[title.TitleSet] = vts;
                }

                if (vts is null)
                {
                    continue;
                }

                var (duration, pgc) = IfoParser.ParseTitleDuration(vts, title.TitleSetTitle);

                // Alguns discos apontam vários títulos para a mesma PGC; mostramos só o primeiro.
                if (!seen.Add((title.TitleSet, pgc)))
                {
                    continue;
                }

                titles.Add(new DvdTitle(title.Number, duration, title.Chapters, title.Angles, title.TitleSet, pgc));
            }

            var label = string.IsNullOrWhiteSpace(source.VolumeLabel) ? fallbackLabel : source.VolumeLabel!;
            return new DvdDisc(label, inputPath, titles);
        }
    }

    private static (IDvdFileSource Source, string InputPath, string Label) Open(string path)
    {
        if (IsIso(path))
        {
            return (new IsoDvdSource(path), path, Path.GetFileNameWithoutExtension(path));
        }

        var videoTs = FindVideoTs(path)
            ?? throw new DirectoryNotFoundException($"Não encontrei a pasta VIDEO_TS em {path}.");

        // O ffmpeg abre a pasta que contém VIDEO_TS (ou a raiz do drive).
        var root = Path.GetDirectoryName(videoTs)!;
        var driveLabel = DriveLabel(root);
        var label = driveLabel ?? new DirectoryInfo(root).Name;
        return (new FolderDvdSource(videoTs, driveLabel), root, label);
    }

    private static bool IsIso(string path) =>
        File.Exists(path) && path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase);

    private static string? FindVideoTs(string directory)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(directory);
        if (string.Equals(Path.GetFileName(trimmed), "VIDEO_TS", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return Directory.EnumerateDirectories(directory)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), "VIDEO_TS", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Rótulo do volume quando a pasta é a raiz de um drive (ex.: D:\), senão null.</summary>
    private static string? DriveLabel(string root)
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
}
