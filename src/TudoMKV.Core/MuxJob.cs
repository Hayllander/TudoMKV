using TudoMKV.Core.Models;

namespace TudoMKV.Core;

/// <summary>Um pedido de gravação: origem, faixas escolhidas e arquivo de saída.</summary>
public sealed class MuxJob
{
    public required MediaSource Source { get; init; }

    public IReadOnlyList<MediaSource> ExtraSources { get; init; } = [];

    public required string OutputPath { get; init; }

    /// <summary>Faixas a gravar, na ordem em que vão aparecer no MKV.</summary>
    public required IReadOnlyList<MediaTrack> Tracks { get; init; }

    public bool KeepChapters { get; init; } = true;

    /// <summary>Entrada que fornece os capítulos quando não vêm da fonte principal.</summary>
    public int ChaptersSourceIndex { get; init; }

    /// <summary>Arquivos temporários que devem ser apagados depois da gravação.</summary>
    public IReadOnlyList<string> TemporaryFiles { get; init; } = [];

    public IReadOnlyList<MediaSource> AllSources => [Source, ..ExtraSources];

    /// <summary>Pasta padrão para títulos de disco quando o usuário não escolhe outra: Vídeos/TudoMKV.</summary>
    public static string DefaultDiscOutputDirectory
    {
        get
        {
            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(videos))
            {
                videos = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return Path.Combine(videos, "TudoMKV");
        }
    }

    /// <summary>
    /// Cria um job com as faixas marcadas da origem, gravando ao lado dela ou em outra pasta.
    /// </summary>
    /// <param name="outputBaseName">
    /// Nome do MKV sem extensão, quando já houver um escolhido (o das releases de cena); nulo mantém o nome
    /// que a própria origem pediu.
    /// </param>
    public static MuxJob FromSelection(
        MediaSource source,
        string? outputDirectory = null,
        bool keepChapters = true,
        string? outputBaseName = null)
    {
        var directory = outputDirectory
            ?? (source.IsDisc ? DefaultDiscOutputDirectory : Path.GetDirectoryName(source.Path) ?? ".");
        var baseName = outputBaseName ?? source.OutputBaseName ?? Path.GetFileNameWithoutExtension(source.Path);
        var output = Path.Combine(directory, baseName + ".mkv");
        if (string.Equals(Path.GetFullPath(output), Path.GetFullPath(source.Path), StringComparison.OrdinalIgnoreCase))
        {
            output = Path.Combine(directory, baseName + ".remux.mkv");
        }

        return new MuxJob
        {
            Source = source,
            OutputPath = output,
            Tracks = source.Tracks.Where(t => t.Selected && t.CanMux).ToList(),
            KeepChapters = keepChapters,
            ChaptersSourceIndex = 0,
        };
    }
}
