using TudoMKV.Core;
using TudoMKV.Core.Models;
using TudoMKV.Disc.Dvd;

namespace TudoMKV.Muxing;

/// <summary>
/// Lista os títulos de um DVD pelos IFO e descreve as faixas de cada um com o demuxer
/// <c>dvdvideo</c> do ffmpeg, que também é quem grava o título depois.
/// </summary>
public sealed class DvdReader(FfprobeInspector inspector) : IDiscReader
{
    public bool CanRead(string path) => DvdScanner.LooksLikeDvd(path);

    public async Task<DiscContents> ReadAsync(string path, TimeSpan minimumLength, CancellationToken cancellationToken = default)
    {
        var disc = await Task.Run(() => DvdScanner.Scan(path), cancellationToken);

        // Como o MakeMKV, escondemos títulos curtos (menus, avisos), a não ser que só existam eles.
        var visible = disc.Titles.Where(t => t.Duration >= minimumLength).ToList();
        if (visible.Count == 0)
        {
            visible = disc.Titles.ToList();
        }

        var sources = new List<MediaSource>();
        foreach (var title in visible)
        {
            sources.Add(await ReadTitleAsync(disc, title, cancellationToken));
        }

        return new DiscContents(disc.Label, sources, disc.Titles.Count - visible.Count);
    }

    private async Task<MediaSource> ReadTitleAsync(DvdDisc disc, DvdTitle title, CancellationToken cancellationToken)
    {
        string[] inputArguments = ["-f", "dvdvideo", "-title", title.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        var probed = await inspector.InspectAsync(disc.InputPath, inputArguments, cancellationToken);

        MarkDefaults(probed.Tracks);

        return new MediaSource
        {
            Path = disc.InputPath,
            InputArguments = inputArguments,
            Name = $"{disc.Label} · Título {title.Number}",
            OutputBaseName = $"{SafeFileName(disc.Label)}_t{title.Number:00}",
            IsDisc = true,
            FormatName = probed.FormatName,
            Duration = probed.Duration > TimeSpan.Zero ? probed.Duration : title.Duration,
            ChapterCount = Math.Max(probed.ChapterCount, title.Chapters),
            Tracks = probed.Tracks,
        };
    }

    /// <summary>O DVD não marca faixas padrão; marcamos o primeiro vídeo e o primeiro áudio.</summary>
    private static void MarkDefaults(IReadOnlyList<MediaTrack> tracks)
    {
        if (tracks.Any(t => t.IsDefault))
        {
            return;
        }

        foreach (var type in new[] { TrackType.Video, TrackType.Audio })
        {
            if (tracks.FirstOrDefault(t => t.Type == type) is { } first)
            {
                first.IsDefault = true;
            }
        }
    }

    internal static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "DVD" : cleaned;
    }
}
