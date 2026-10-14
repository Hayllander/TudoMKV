using System.Globalization;
using TudoMKV.Core;
using TudoMKV.Core.Models;

namespace TudoMKV.App.ViewModels;

/// <summary>Textos curtos da árvore e do painel de informações, no estilo do MakeMKV.</summary>
public static class Describe
{
    private static readonly Dictionary<string, string> Languages = new()
    {
        ["por"] = "Português",
        ["eng"] = "Inglês",
        ["spa"] = "Espanhol",
        ["fra"] = "Francês",
        ["fre"] = "Francês",
        ["deu"] = "Alemão",
        ["ger"] = "Alemão",
        ["ita"] = "Italiano",
        ["jpn"] = "Japonês",
        ["zho"] = "Chinês",
        ["chi"] = "Chinês",
        ["kor"] = "Coreano",
        ["rus"] = "Russo",
        ["nld"] = "Holandês",
        ["dut"] = "Holandês",
        ["pol"] = "Polonês",
        ["tur"] = "Turco",
        ["ara"] = "Árabe",
        ["heb"] = "Hebraico",
        ["hin"] = "Hindi",
        ["tha"] = "Tailandês",
        ["swe"] = "Sueco",
        ["dan"] = "Dinamarquês",
        ["nor"] = "Norueguês",
        ["fin"] = "Finlandês",
        ["ces"] = "Tcheco",
        ["cze"] = "Tcheco",
        ["hun"] = "Húngaro",
        ["ell"] = "Grego",
        ["gre"] = "Grego",
        ["und"] = "Indefinido",
    };

    private static readonly Dictionary<string, string> Codecs = new()
    {
        ["mpeg2video"] = "Mpeg2",
        ["mpeg1video"] = "Mpeg1",
        ["h264"] = "MPEG4-AVC",
        ["hevc"] = "MPEG-H HEVC",
        ["vc1"] = "VC-1",
        ["ac3"] = "AC3",
        ["eac3"] = "E-AC3",
        ["truehd"] = "TrueHD",
        ["dts"] = "DTS",
        ["pcm_bluray"] = "LPCM",
        ["pcm_dvd"] = "LPCM",
        ["mp2"] = "Mpeg Audio",
        ["aac"] = "AAC",
        ["hdmv_pgs_subtitle"] = "PGS",
        ["dvd_subtitle"] = "Legendas DVD",
        ["subrip"] = "SRT",
        ["mov_text"] = "Texto (MP4)",
        ["ass"] = "ASS",
    };

    public static string KindName(SourceKind kind) => kind switch
    {
        SourceKind.Dvd => "Disco DVD",
        SourceKind.Bluray => "Disco Blu-ray",
        _ => "Arquivos",
    };

    public static string Language(string code) =>
        Languages.TryGetValue(code, out var name) ? name : code;

    public static string Codec(string codec) =>
        Codecs.TryGetValue(codec, out var name) ? name : codec.ToUpperInvariant();

    public static string Size(long bytes) => bytes switch
    {
        <= 0 => "",
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB",
        _ => (bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
    };

    public static string Duration(TimeSpan duration) => duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);

    public static string Channels(int? channels) => channels switch
    {
        null => "",
        1 => "1.0",
        2 => "2.0",
        6 => "5.1",
        8 => "7.1",
        _ => $"{channels} canais",
    };

    public static string TrackTypeName(TrackType type) => type switch
    {
        TrackType.Video => "Vídeo",
        TrackType.Audio => "Áudio",
        TrackType.Subtitle => "Legendas",
        TrackType.Attachment => "Anexo",
        _ => "Dados",
    };

    public static string Title(MediaSource source, string label)
    {
        var parts = new List<string>();
        if (source.ChapterCount > 0)
        {
            parts.Add($"{source.ChapterCount} capítulo(s)");
        }

        parts.Add(Duration(source.Duration));
        if (Size(source.SizeBytes) is { Length: > 0 } size)
        {
            parts.Add(size);
        }

        return $"{label} - {string.Join(", ", parts)}";
    }

    public static string Track(MediaTrack track)
    {
        var parts = new List<string> { Codec(track.Codec) };
        switch (track.Type)
        {
            case TrackType.Video when track.Width is not null:
                parts.Add($"{track.Width}x{track.Height}");
                break;
            case TrackType.Audio:
                parts.Add(Language(track.Language));
                parts.Add(Channels(track.Channels));
                break;
            case TrackType.Subtitle:
                parts.Add(Language(track.Language));
                break;
        }

        if (track.IsForced)
        {
            parts.Add("(forçada)");
        }

        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    public static string TrackInfo(MediaTrack track)
    {
        var lines = new List<string>
        {
            $"Informações da Faixa",
            $"Tipo: {TrackTypeName(track.Type)}",
            $"Codec: {Codec(track.Codec)} ({track.Codec})",
        };
        if (track.Type is TrackType.Audio or TrackType.Subtitle)
        {
            lines.Add($"Idioma: {Language(track.Language)} ({track.Language})");
        }

        if (track.Width is not null)
        {
            lines.Add($"Resolução: {track.Width}x{track.Height}");
        }

        if (track.Channels is not null)
        {
            lines.Add($"Canais: {Channels(track.Channels)}");
        }

        if (track.SampleRate is not null)
        {
            lines.Add($"Taxa de amostragem: {track.SampleRate} Hz");
        }

        if (!string.IsNullOrEmpty(track.Title))
        {
            lines.Add($"Nome: {track.Title}");
        }

        if (track.StreamId is not null)
        {
            lines.Add($"PID: 0x{track.StreamId:X4}");
        }

        lines.Add($"Faixa padrão: {(track.IsDefault ? "sim" : "não")}");
        return string.Join('\n', lines);
    }
}
