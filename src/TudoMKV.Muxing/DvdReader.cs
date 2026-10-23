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
        DiscTracks.EncodePcm(probed.Tracks, SourceKind.Dvd);

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
            Tracks = DiscTracks.WithMonoToStereo(probed.Tracks, SourceKind.Dvd),
        };
    }
}

/// <summary>Regras comuns a DVD e Blu-ray.</summary>
internal static class DiscTracks
{
    /// <summary>
    /// Deixa as faixas de uma origem - disco ou arquivo - prontas para a gravação: padrões, idioma em três
    /// letras, nomes de áudio e legenda e origem. O nome das faixas de áudio e legenda é sempre reescrito no
    /// padrão do ShawScope-Audio, mesmo que a origem já traga um; os outros tipos ficam como estão.
    /// </summary>
    public static void Prepare(IReadOnlyList<MediaTrack> tracks, SourceKind kind)
    {
        MarkDefaults(tracks);
        foreach (var track in tracks)
        {
            // O idioma vai em três letras, como o contêiner o guarda: "pt" vira "por" e "en" vira "eng".
            track.Language = TrackNaming.Language(track.Language);

            // No áudio o nome é "English AC3 5.1" e na legenda "Portuguese (Subtitles)", como o
            // ShawScope-Audio grava.
            track.Title = track.Type switch
            {
                TrackType.Audio => TrackNaming.AudioTitle(track),
                TrackType.Subtitle => TrackNaming.SubtitleTitle(track.Language),
                _ => track.Title,
            };

            // Arquivo não tem PID de disco: fica o SOURCE_ID que ele já traz (o de um MKV do MakeMKV).
            track.SourceId = SourceId(kind, track.StreamId) ?? track.SourceId;
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

    /// <summary>
    /// Acrescenta, logo depois de cada áudio mono, a faixa estéreo 2.0 que o TudoMKV gera na gravação
    /// (o mono continua no MKV). O estéreo novo herda a marca de faixa padrão do mono de onde sai.
    /// </summary>
    /// <param name="kind">Origem das faixas: o áudio sem perda do Blu-ray gera estéreo em FLAC.</param>
    public static IReadOnlyList<MediaTrack> WithMonoToStereo(IReadOnlyList<MediaTrack> tracks, SourceKind kind)
    {
        if (!tracks.Any(NeedsStereoAlternative))
        {
            return tracks;
        }

        var result = new List<MediaTrack>(tracks.Count + 1);
        foreach (var track in tracks)
        {
            result.Add(track);
            if (NeedsStereoAlternative(track))
            {
                result.Add(StereoAlternative(track, ConversionFor(kind, track)));
            }
        }

        return result;
    }

    /// <summary>
    /// Áudio mono que pede uma faixa estéreo extra, seja qual for o codec de origem: o mono continua no MKV
    /// como está e a faixa 2.0 sai do mesmo áudio, em FLAC no Blu-ray e em AC-3 no DVD.
    /// </summary>
    private static bool NeedsStereoAlternative(MediaTrack track) =>
        track.Type == TrackType.Audio && track.Channels == 1;

    /// <summary>
    /// Codec da faixa estéreo gerada. O áudio sem perda do Blu-ray (LPCM, TrueHD) vira FLAC, para a cópia
    /// não perder qualidade; o resto - áudio de DVD e o que já tem perda no Blu-ray - vira AC-3 2.0, porque
    /// reencodar um áudio com perda em FLAC só aumentaria o arquivo sem devolver qualidade.
    /// </summary>
    private static AudioConversion ConversionFor(SourceKind kind, MediaTrack track) =>
        kind == SourceKind.Bluray && IsLossless(track)
            ? AudioConversion.MonoToStereoFlac
            : AudioConversion.MonoToStereo;

    /// <summary>
    /// Codecs sem perda que a gravação pode receber: LPCM (pcm_*), TrueHD/MLP, FLAC e o DTS-HD MA, que o
    /// ffprobe reporta como <c>dts</c> e só se distingue do DTS comum pelo perfil.
    /// </summary>
    private static bool IsLossless(MediaTrack track) =>
        track.Codec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase)
        || LosslessCodecs.Contains(track.Codec)
        || track.Profile?.Contains("DTS-HD MA", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// O PCM não entra no MKV. O do Blu-ray - e o de arquivo, que vem de um remux de Blu-ray - vira FLAC sem
    /// perda, com canais, taxa e profundidade como estão, porque a origem não tem perda. O do DVD vira AC-3,
    /// que é a família do áudio do disco, entrando no MKV a 192 kbps em vez de 1,5 Mb/s.
    /// </summary>
    public static void EncodePcm(IReadOnlyList<MediaTrack> tracks, SourceKind kind)
    {
        var encoding = kind == SourceKind.Dvd ? AudioEncoding.Ac3 : AudioEncoding.Flac;
        foreach (var track in tracks)
        {
            if (track.Type == TrackType.Audio && track.Codec.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase))
            {
                track.EncodeTo = encoding;
            }
        }
    }

    private static readonly HashSet<string> LosslessCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "truehd",
        "mlp",
        "flac",
        "alac",
    };

    private static MediaTrack StereoAlternative(MediaTrack mono, AudioConversion conversion)
    {
        // A marca de padrão passa do mono para o estéreo dele, e só dele: um segundo mono (comentário,
        // outro idioma) não pode deixar o MKV com dois áudios padrão.
        var isDefault = mono.IsDefault;
        mono.IsDefault = false;
        var stereo = new MediaTrack
        {
            InputIndex = mono.InputIndex,
            Index = mono.Index,
            Type = TrackType.Audio,
            Codec = conversion.Codec,
            Language = mono.Language,
            IsDefault = isDefault,
            Channels = 2,
            SampleRate = mono.SampleRate,
            Conversion = conversion,
            ConvertedFrom = mono,
        };

        // A faixa gerada nasce com o nome do script, o mesmo das outras faixas de áudio.
        stereo.Title = TrackNaming.AudioTitle(stereo);
        return stereo;
    }

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

    /// <summary>
    /// Capítulos em FFMETADATA, de cada início até o próximo (o último vai até o fim do título). O nome sai
    /// em inglês, como o MakeMKV grava: o conteúdo do MKV não depende do idioma da interface.
    /// </summary>
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

            builder.Append(CultureInfo.InvariantCulture, $"[CHAPTER]\nTIMEBASE=1/1000\nSTART={start}\nEND={end}\ntitle=Chapter {i + 1}\n");
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
