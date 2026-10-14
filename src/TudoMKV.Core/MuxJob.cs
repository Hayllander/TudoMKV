using TudoMKV.Core.Models;

namespace TudoMKV.Core;

/// <summary>Um pedido de gravação: origem, faixas escolhidas e arquivo de saída.</summary>
public sealed class MuxJob
{
    public required MediaSource Source { get; init; }

    public required string OutputPath { get; init; }

    /// <summary>Faixas a gravar, na ordem em que vão aparecer no MKV.</summary>
    public required IReadOnlyList<MediaTrack> Tracks { get; init; }

    public bool KeepChapters { get; init; } = true;

    /// <summary>Cria um job com as faixas marcadas da origem, gravando ao lado dela ou em outra pasta.</summary>
    public static MuxJob FromSelection(MediaSource source, string? outputDirectory = null)
    {
        var directory = outputDirectory ?? Path.GetDirectoryName(source.Path) ?? ".";
        var output = Path.Combine(directory, Path.GetFileNameWithoutExtension(source.Path) + ".mkv");
        if (string.Equals(Path.GetFullPath(output), Path.GetFullPath(source.Path), StringComparison.OrdinalIgnoreCase))
        {
            output = Path.Combine(directory, Path.GetFileNameWithoutExtension(source.Path) + ".remux.mkv");
        }

        return new MuxJob
        {
            Source = source,
            OutputPath = output,
            Tracks = source.Tracks.Where(t => t.Selected && t.CanMux).ToList(),
        };
    }
}
