using System.Globalization;
using TudoMKV.Core;
using TudoMKV.Core.Models;

namespace TudoMKV.Muxing;

/// <summary>
/// Nomes e idiomas das faixas, com as regras do <c>ShawScope-Audio.cmd</c>: o áudio é nomeado
/// "English DTS-HD MA 5.1", a legenda "Portuguese (Subtitles)" e o idioma vai em três letras (por, eng).
/// Nomes e idiomas ficam em inglês, como o resto do conteúdo do MKV.
/// O <see cref="Report(MediaSource)"/> é a conferência que o script imprime, faixa por faixa, no log.
/// </summary>
public static class TrackNaming
{
    /// <summary>Sigla de duas letras para a de três que o contêiner usa; o resto passa como está.</summary>
    private static readonly Dictionary<string, string> ThreeLetterCodes = new(StringComparer.Ordinal)
    {
        ["en"] = "eng",
        ["pt"] = "por",
        ["es"] = "spa",
        ["fr"] = "fra",
        ["de"] = "ger",
        ["it"] = "ita",
        ["ru"] = "rus",
        ["ja"] = "jpn",
        ["zh"] = "chi",
        ["ko"] = "kor",
        ["und"] = "und",
    };

    /// <summary>
    /// Nome do idioma em inglês, que é o idioma de tudo o que entra no MKV. As variantes que outros muxers
    /// gravam (fre, deu, zho) entram pelas mesmas letras, e o que não estiver aqui sai como o próprio código.
    /// </summary>
    private static readonly Dictionary<string, string> LanguageNames = new(StringComparer.Ordinal)
    {
        ["eng"] = "English",
        ["por"] = "Portuguese",
        ["spa"] = "Spanish",
        ["fra"] = "French",
        ["fre"] = "French",
        ["ger"] = "German",
        ["deu"] = "German",
        ["ita"] = "Italian",
        ["rus"] = "Russian",
        ["jpn"] = "Japanese",
        ["chi"] = "Chinese",
        ["zho"] = "Chinese",
        ["kor"] = "Korean",
        ["und"] = "Unknown",
    };

    /// <summary>Sigla de três letras do idioma, como o Matroska a guarda; vazio vira "und".</summary>
    public static string Language(string? language)
    {
        var code = (language ?? string.Empty).Trim().ToLowerInvariant();
        if (code.Length == 0)
        {
            return "und";
        }

        return ThreeLetterCodes.TryGetValue(code, out var threeLetter) ? threeLetter : code;
    }

    /// <summary>Nome do idioma para o nome da faixa e para o resumo do log.</summary>
    public static string LanguageName(string? language)
    {
        var code = Language(language);
        return LanguageNames.TryGetValue(code, out var name) ? name : code;
    }

    /// <summary>Codec curto do nome da faixa: "DTS-HD MA", "AC3", "DDP", "TrueHD", "FLAC", "PCM"...</summary>
    public static string CodecShort(string codec, string? profile)
    {
        var name = codec.ToLowerInvariant();
        return name switch
        {
            "eac3" => "DDP",
            "ac3" => "AC3",
            "truehd" => "TrueHD",
            "flac" => "FLAC",
            "aac" => "AAC",
            "alac" => "ALAC",
            "opus" => "Opus",
            "vorbis" => "Vorbis",
            "mp3" => "MP3",
            "dts" => DtsShort(profile),
            _ when name.StartsWith("pcm", StringComparison.Ordinal) => "PCM",
            _ => name.Length > 0 ? name.ToUpperInvariant() : "Audio",
        };
    }

    /// <summary>
    /// Nome longo do codec, como o script o mostra na coluna do resumo. O DTS-HD MA só se distingue
    /// do DTS comum pelo perfil, porque o ffprobe reporta os dois como <c>dts</c>.
    /// </summary>
    public static string CodecLabel(string codec, string? profile, int? channels, string? codecLongName)
    {
        var name = codec.ToLowerInvariant();
        if (name.Contains("dts", StringComparison.Ordinal))
        {
            return Match(profile, "DTS-HD MA", "DTS-HD Master") ? "DTS-HD Master Audio" : DtsShort(profile);
        }

        switch (name)
        {
            case "eac3":
                return Match(profile, "Atmos") ? "Dolby Digital Plus + Atmos" : "Dolby Digital Plus";
            case "ac3":
                if (Match(profile, "Atmos"))
                {
                    return "Dolby Digital + Atmos";
                }

                if (Match(profile, "Digital EX"))
                {
                    return "Dolby Digital EX";
                }

                if (Match(profile, "Pro Logic II"))
                {
                    return "Dolby Pro Logic II";
                }

                if (Match(profile, "Dolby Surround EX"))
                {
                    return "Dolby Surround EX";
                }

                if (Match(profile, "Dolby Surround"))
                {
                    return "Dolby Surround";
                }

                return Match(profile, "Dolby Stereo") ? "Dolby Stereo" : "Dolby Digital";
            case "truehd":
                return Match(profile, "Atmos") ? "Dolby TrueHD + Atmos" : "Dolby TrueHD";
            case "aac":
                return "AAC";
            case "flac":
                return "FLAC";
            case "alac":
                return "Apple Lossless (ALAC)";
            case "opus":
                return "Opus";
            case "vorbis":
                return "Vorbis";
            case "mp3":
                return "MP3";
            case "mpegh3da":
                return "MPEG-H 3D Audio";
        }

        if (name.StartsWith("pcm", StringComparison.Ordinal))
        {
            return channels switch
            {
                >= 6 => "PCM Multichannel",
                2 => "PCM Stereo",
                1 => "PCM Mono",
                _ => "PCM",
            };
        }

        if (name.StartsWith("auro", StringComparison.Ordinal))
        {
            return "Auro-3D";
        }

        if (!string.IsNullOrWhiteSpace(codecLongName))
        {
            return codecLongName;
        }

        return name.Length > 0 ? codec : "unknown";
    }

    /// <summary>Leiaute dos canais no resumo: "Mono", "Stereo", "5.1", "7.1".</summary>
    public static string AudioLayout(int? channels) => channels switch
    {
        1 => "Mono",
        2 => "Stereo",
        6 => "5.1",
        8 => "7.1",
        null => "N/A",
        _ => $"{channels} ch",
    };

    /// <summary>Canais no nome da faixa, no formato que o script grava: 1.0, 2.0, 5.1, 7.1.</summary>
    public static string ChannelPretty(int? channels) => channels switch
    {
        1 => "1.0",
        2 => "2.0",
        6 => "5.1",
        8 => "7.1",
        null => "N/A",
        _ => $"{channels}ch",
    };

    /// <summary>Nome da faixa de áudio gravada no MKV: "English DTS-HD MA 5.1".</summary>
    public static string AudioTitle(MediaTrack track) =>
        $"{LanguageName(track.Language)} {CodecShort(OutputCodec(track), track.Profile)} {ChannelPretty(track.Channels)}";

    /// <summary>
    /// Nome da faixa de legenda gravada no MKV: "Portuguese (Subtitles)", "English (Subtitles)".
    /// </summary>
    public static string SubtitleTitle(string? language) => $"{LanguageName(language)} (Subtitles)";

    /// <summary>O resumo por faixa de áudio, na ordem em que as faixas estão no MKV.</summary>
    public static IReadOnlyList<string> Report(MediaSource source) =>
        source.Tracks
            .Where(track => track.Type == TrackType.Audio)
            .Select((track, index) => Report(track, index + 1))
            .ToList();

    /// <summary>A linha de uma faixa, como o script a imprime: "Track 1: Chinese / DTS-HD Master Audio / ...".</summary>
    internal static string Report(MediaTrack track, int number)
    {
        var depth = track.BitsPerSample is { } bits ? $"{bits}-bit" : "N/A";
        var title = string.IsNullOrEmpty(track.Title) ? string.Empty : $" / \"{track.Title}\"";
        var codec = CodecLabel(track.Codec, track.Profile, track.Channels, track.CodecLongName);
        return $"Track {number}: {LanguageName(track.Language)} / {codec} / {AudioLayout(track.Channels)}"
            + $" / {SampleRate(track.SampleRate)} / {BitRate(track.BitRate)} / {depth}{title}";
    }

    /// <summary>O codec que a faixa terá no MKV: o PCM vira FLAC no Blu-ray e AC-3 no DVD.</summary>
    private static string OutputCodec(MediaTrack track) => track.EncodeTo?.Codec ?? track.Codec;

    private static string DtsShort(string? profile) => Match(profile, "DTS-HD MA", "DTS-HD Master")
        ? "DTS-HD MA"
        : Match(profile, "DTS-HD") ? "DTS-HD" : "DTS";

    /// <summary>O mesmo que o <c>-match</c> do PowerShell: trecho em qualquer posição, sem diferenciar maiúsculas.</summary>
    private static bool Match(string? text, params string[] patterns) =>
        text is not null && patterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static string SampleRate(int? hertz) =>
        hertz is { } value ? (value / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " kHz" : "N/A";

    private static string BitRate(long? bitsPerSecond) =>
        bitsPerSecond is { } value
            ? Math.Round(value / 1000.0).ToString("0", CultureInfo.InvariantCulture) + " kb/s"
            : "N/A";
}
