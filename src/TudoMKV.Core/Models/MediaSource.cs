namespace TudoMKV.Core.Models;

/// <summary>Um arquivo (ou, mais adiante, um título de disco) que pode virar MKV.</summary>
public sealed class MediaSource
{
    public required string Path { get; init; }

    public string? FormatName { get; init; }

    public TimeSpan Duration { get; init; }

    public int ChapterCount { get; init; }

    public required IReadOnlyList<MediaTrack> Tracks { get; init; }

    public string DisplayName => System.IO.Path.GetFileName(Path);
}
