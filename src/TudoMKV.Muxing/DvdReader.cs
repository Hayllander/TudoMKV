using System.Globalization;
using System.Text;
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

        // Os capítulos do demuxer dvdvideo contam o tempo do IFO NTSC sem o fator 1,001 e vão se adiantando
        // (quase 4 s no fim de 1 h); gravamos os nossos, pelas células, como o MakeMKV.
        string? chapters = null;
        if (title.ChapterStarts.Count > 0)
        {
            chapters = Path.Combine(BlurayReader.WorkDirectory(disc.InputPath), $"dvd_t{title.Number:00}.chapters.txt");
            await File.WriteAllTextAsync(chapters, DiscTracks.BuildChapters(title.ChapterStarts, title.Duration), cancellationToken);
        }

        return new MediaSource
        {
            Path = disc.InputPath,
            InputArguments = inputArguments,
            ChaptersFile = chapters,
            Name = $"{disc.Label} · Título {title.Number}",
            TitleNumber = title.Number,
            DiscTitleSet = title.TitleSet,
            OutputBaseName = $"{DiscTracks.SafeFileName(disc.Label)}_t{title.Number:00}",
            IsDisc = true,
            FormatName = probed.FormatName,
            Duration = title.Duration > TimeSpan.Zero ? title.Duration : probed.Duration,
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

    /// <summary>Capítulos em FFMETADATA, de cada início até o próximo (o último vai até o fim do título).</summary>
    public static string BuildChapters(IReadOnlyList<TimeSpan> starts, TimeSpan total)
    {
        var builder = new StringBuilder(";FFMETADATA1\n");
        for (var i = 0; i < starts.Count; i++)
        {
            var start = (long)(starts[i].TotalSeconds * 1000);
            var end = (long)((i + 1 < starts.Count ? starts[i + 1] : total).TotalSeconds * 1000);
            if (end <= start)
            {
                continue;
            }

            builder.Append(CultureInfo.InvariantCulture, $"[CHAPTER]\nTIMEBASE=1/1000\nSTART={start}\nEND={end}\ntitle=Capítulo {i + 1:00}\n");
        }

        return builder.ToString();
    }

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "disco" : cleaned;
    }
}
