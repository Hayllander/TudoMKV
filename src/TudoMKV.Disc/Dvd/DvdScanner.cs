using TudoMKV.Disc.Bluray;

namespace TudoMKV.Disc.Dvd;

public sealed record DvdTitle(int Number, TimeSpan Duration, int Chapters, int Angles, int TitleSet, int Pgc, int Cells, long SizeBytes)
{
    /// <summary>Início de cada capítulo pelos IFO, contado desde o começo do título.</summary>
    public IReadOnlyList<TimeSpan> ChapterStarts { get; init; } = [];
}

/// <summary>Um título que não entrou na lista, e por quê (para o log).</summary>
public sealed record DvdSkip(int Number, string Reason);

/// <summary>Resultado da leitura dos IFO: rótulo, caminho que o ffmpeg deve abrir, títulos e pulados.</summary>
public sealed record DvdDisc(string Label, string InputPath, IReadOnlyList<DvdTitle> Titles, IReadOnlyList<DvdSkip> Skipped);

/// <summary>
/// Lista os títulos de um DVD a partir do VIDEO_TS.IFO, da pasta VIDEO_TS, da pasta que a contém,
/// da raiz de um drive ou de uma ISO.
/// </summary>
public static class DvdScanner
{
    public static bool LooksLikeDvd(string path) =>
        IsIso(path) || IsIfo(path) || (Directory.Exists(path) && FindVideoTs(path) is not null);

    public static DvdDisc Scan(string path, TimeSpan minimumLength)
    {
        var (source, inputPath, fallbackLabel) = Open(path);
        using (source)
        {
            var vmg = source.ReadVideoTsFile("VIDEO_TS.IFO")
                ?? throw new InvalidDataException("VIDEO_TS.IFO não encontrado.");

            var titles = new List<DvdTitle>();
            var skipped = new List<DvdSkip>();
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
                    skipped.Add(new DvdSkip(title.Number, $"o arquivo VTS_{title.TitleSet:00}_0.IFO não existe"));
                    continue;
                }

                TitleInfo info;
                try
                {
                    info = IfoParser.ParseTitle(vts, title.TitleSetTitle);
                }
                catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or IndexOutOfRangeException)
                {
                    skipped.Add(new DvdSkip(title.Number, "suas tabelas não puderam ser lidas"));
                    continue;
                }

                if (info.Duration < minimumLength)
                {
                    skipped.Add(new DvdSkip(title.Number, $"sua duração é de {(int)info.Duration.TotalSeconds} segundos (mínimo {(int)minimumLength.TotalSeconds} segs.)"));
                    continue;
                }

                // Alguns discos apontam vários títulos para a mesma PGC; mostramos só o primeiro.
                if (!seen.Add((title.TitleSet, info.FirstPgc)))
                {
                    skipped.Add(new DvdSkip(title.Number, "é uma cópia de outro título"));
                    continue;
                }

                titles.Add(new DvdTitle(title.Number, info.Duration, title.Chapters, title.Angles, title.TitleSet, info.FirstPgc, info.Cells, info.Sectors * 2048)
                {
                    ChapterStarts = info.ChapterStarts,
                });
            }

            var label = IsMeaningfulLabel(source.VolumeLabel) ? source.VolumeLabel! : fallbackLabel;
            return new DvdDisc(label, inputPath, titles, skipped);
        }
    }

    private static (IDvdFileSource Source, string InputPath, string Label) Open(string path)
    {
        if (IsIso(path))
        {
            var source = new IsoDvdSource(path);
            return (source, source.InputPath, Path.GetFileNameWithoutExtension(path));
        }

        if (IsIfo(path))
        {
            path = Path.GetDirectoryName(Path.GetFullPath(path))!;
        }

        var videoTs = FindVideoTs(path)
            ?? throw new DirectoryNotFoundException($"Não encontrei a pasta VIDEO_TS em {path}.");

        // O ffmpeg abre a pasta que contém VIDEO_TS (ou a raiz do drive).
        var root = Path.GetDirectoryName(videoTs)!;
        var driveLabel = BlurayScanner.DriveLabel(root);
        var label = driveLabel ?? new DirectoryInfo(root).Name;
        var folderSource = new FolderDvdSource(videoTs, root, driveLabel);
        return (folderSource, folderSource.InputPath, label);
    }

    /// <summary>
    /// Rótulos vazios ou só com números (ex.: "20110204_1906", a data que alguns programas gravam como
    /// rótulo da ISO) não dizem nada; nesse caso vale o nome do arquivo ou da pasta.
    /// </summary>
    internal static bool IsMeaningfulLabel(string? label) =>
        !string.IsNullOrWhiteSpace(label) && label.Any(char.IsLetter);

    private static bool IsIso(string path) =>
        File.Exists(path) && (path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".img", StringComparison.OrdinalIgnoreCase));

    private static bool IsIfo(string path) =>
        File.Exists(path) && Path.GetFileName(path).Equals("VIDEO_TS.IFO", StringComparison.OrdinalIgnoreCase);

    private static string? FindVideoTs(string directory)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (string.Equals(Path.GetFileName(trimmed), "VIDEO_TS", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return Directory.EnumerateDirectories(trimmed)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), "VIDEO_TS", StringComparison.OrdinalIgnoreCase));
    }
}
