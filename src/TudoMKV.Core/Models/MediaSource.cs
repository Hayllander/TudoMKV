namespace TudoMKV.Core.Models;

/// <summary>Um arquivo ou um título de disco que pode virar MKV.</summary>
public sealed class MediaSource
{
    /// <summary>Caminho que o ffmpeg abre: o arquivo, a pasta do DVD, a ISO ou o drive.</summary>
    public required string Path { get; init; }

    /// <summary>Opções de entrada do ffmpeg antes de <c>-i</c> (ex.: <c>-f dvdvideo -title 3</c>).</summary>
    public IReadOnlyList<string> InputArguments { get; init; } = [];

    /// <summary>Nome para exibir; se vazio, usa o nome do arquivo.</summary>
    public string? Name { get; init; }

    /// <summary>Nome do MKV sem extensão; se vazio, usa o nome do arquivo de origem.</summary>
    public string? OutputBaseName { get; init; }

    /// <summary>Títulos de disco não são gravados ao lado da origem, que costuma ser somente leitura.</summary>
    public bool IsDisc { get; init; }

    public string? FormatName { get; init; }

    public TimeSpan Duration { get; init; }

    public int ChapterCount { get; init; }

    public required IReadOnlyList<MediaTrack> Tracks { get; init; }

    public string DisplayName => Name ?? System.IO.Path.GetFileName(Path);

    /// <summary>Identifica a origem: o mesmo caminho com opções diferentes é outro título.</summary>
    public string Key => InputArguments.Count == 0 ? Path : $"{Path}|{string.Join(' ', InputArguments)}";
}
