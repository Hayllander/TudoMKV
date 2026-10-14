using System.Globalization;
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
        var disc = await Task.Run(() => DvdScanner.Scan(path, minimumLength), cancellationToken);
        var messages = new List<string> { $"Abrindo DVD em {path}" };
        messages.AddRange(disc.Skipped.Select(s => $"Título #{s.Number} foi pulado pois {s.Reason}"));

        var sources = new List<MediaSource>();
        foreach (var title in disc.Titles)
        {
            sources.Add(await ReadTitleAsync(disc, title, cancellationToken));
            messages.Add($"Título #{title.Number} foi adicionado ({title.Cells} célula(s), {title.Duration:h\\:mm\\:ss})");
        }

        return new DiscContents(SourceKind.Dvd, disc.Label, sources, messages);
    }

    private async Task<MediaSource> ReadTitleAsync(DvdDisc disc, DvdTitle title, CancellationToken cancellationToken)
    {
        string[] inputArguments = ["-f", "dvdvideo", "-title", title.Number.ToString(CultureInfo.InvariantCulture), "-trim", "0"];
        var probed = await inspector.InspectAsync(disc.InputPath, inputArguments, cancellationToken);

        DiscTracks.Prepare(probed.Tracks, SourceKind.Dvd);

        return new MediaSource
        {
            Path = disc.InputPath,
            InputArguments = inputArguments,
            Name = $"{disc.Label} · Título {title.Number}",
            OutputBaseName = $"{DiscTracks.SafeFileName(disc.Label)}_t{title.Number:00}",
            IsDisc = true,
            FormatName = probed.FormatName,
            Duration = probed.Duration > TimeSpan.Zero ? probed.Duration : title.Duration,
            ChapterCount = Math.Max(probed.ChapterCount, title.Chapters),
            SizeBytes = title.SizeBytes,
            Tracks = probed.Tracks,
        };
    }
}

/// <summary>Regras comuns a DVD e Blu-ray.</summary>
internal static class DiscTracks
{
    /// <summary>Deixa as faixas de um título como o MakeMKV grava: padrões, nomes de áudio e origem.</summary>
    public static void Prepare(IReadOnlyList<MediaTrack> tracks, SourceKind kind)
    {
        MarkDefaults(tracks);
        foreach (var track in tracks)
        {
            if (track.Type == TrackType.Audio && string.IsNullOrEmpty(track.Title))
            {
                track.Title = AudioTitle(track.Channels);
            }

            track.SourceId = SourceId(kind, track.StreamId);
        }
    }

    /// <summary>Discos não marcam faixas padrão; marcamos o primeiro vídeo e o primeiro áudio.</summary>
    public static void MarkDefaults(IReadOnlyList<MediaTrack> tracks)
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

    /// <summary>Os mesmos nomes que o MakeMKV dá às faixas de áudio.</summary>
    public static string? AudioTitle(int? channels) => channels switch
    {
        1 => "Mono",
        2 => "Stereo",
        6 => "Surround 5.1",
        8 => "Surround 7.1",
        _ => null,
    };

    /// <summary>
    /// Blu-ray: "00" + PID (0x1011 vira "001011"). DVD: "01" + subfluxo + fluxo; o demuxer dvdvideo numera
    /// o vídeo 0x1E0 e o MPEG áudio 0x1C0 (fluxo próprio) e o AC3, DTS, LPCM e legendas pelo subfluxo do 0xBD.
    /// </summary>
    public static string? SourceId(SourceKind kind, int? streamId) => (kind, streamId) switch
    {
        (SourceKind.Bluray, { } pid) when pid is > 0 and <= 0xFFFF => $"00{pid:X4}",
        (SourceKind.Dvd, { } id) when id is >= 0x100 and <= 0x1FF => $"0100{id & 0xFF:X2}",
        (SourceKind.Dvd, { } id) when id is >= 0 and <= 0xFF => $"01{id:X2}BD",
        _ => null,
    };

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "disco" : cleaned;
    }
}
