using System.Text;
using TudoMKV.Core;
using TudoMKV.Core.Models;

namespace TudoMKV.App.Integration;

/// <summary>
/// Nome do MKV no formato das releases de cena, usado quando o filme foi identificado no TMDb ou no IMDb.
/// DVD e Blu-ray de disco seguem os exemplos do padrão "Blu-ray RAW and REMUX releases"; arquivos soltos,
/// que vêm de um remux de Blu-ray, seguem o mesmo formato do Blu-ray.
/// </summary>
public static class ReleaseName
{
    /// <summary>Blu-ray: <c>Título.Ano.1080p.BluRay.REMUX.Vídeo.Áudio-Grupo</c>.</summary>
    private const string BluraySource = "BluRay";

    /// <summary>DVD: <c>Título.Ano.DVD.REMUX.Áudio.Vídeo-Grupo</c>, sem a resolução.</summary>
    private const string DvdSource = "DVD";

    private const string RipType = "REMUX";

    public static string Build(MovieInfo movie, MediaSource source, SourceKind kind, string group)
    {
        var video = source.Tracks.FirstOrDefault(track => track.Type == TrackType.Video && track.CanMux);
        var audio = DefaultAudio(source.Tracks);

        var parts = new List<string> { Slug(movie.Title) };
        if (movie.Year.Length > 0)
        {
            parts.Add(movie.Year);
        }

        if (kind == SourceKind.Dvd)
        {
            parts.Add(DvdSource);
            parts.Add(RipType);
            Add(parts, AudioToken(audio));
            Add(parts, VideoToken(video));
        }
        else
        {
            Add(parts, Quality(video));
            parts.Add(BluraySource);
            parts.Add(RipType);
            Add(parts, VideoToken(video));
            Add(parts, AudioToken(audio));
        }

        var name = string.Join('.', parts.Where(part => part.Length > 0));
        return group.Length > 0 ? $"{name}-{group}" : name;
    }

    /// <summary>
    /// A faixa de áudio que assina o nome: a padrão entre as marcadas, porque as geradas na gravação (o
    /// estéreo 2.0 do mono) assumem essa marca. A só estéreo do DVD sai daqui como o "DD2.0" do exemplo.
    /// </summary>
    private static MediaTrack? DefaultAudio(IReadOnlyList<MediaTrack> tracks) =>
        tracks
            .Where(track => track.Type == TrackType.Audio && track.CanMux)
            .OrderByDescending(track => track.Selected)
            .ThenByDescending(track => track.IsDefault)
            .FirstOrDefault();

    /// <summary>Codec de saída da faixa, já contando o que a gravação reencoda (o PCM vira FLAC ou AC-3).</summary>
    private static string? AudioToken(MediaTrack? audio)
    {
        if (audio is null)
        {
            return null;
        }

        var codec = (audio.EncodeTo?.Codec ?? audio.Codec).ToLowerInvariant();
        var prefix = codec switch
        {
            "ac3" => "DD",
            "eac3" => "DDP",
            "truehd" => "TrueHD",
            "dts" => DtsToken(audio.Profile),
            "flac" => "FLAC",
            "aac" => "AAC",
            "mp2" => "MP2",
            "mp3" => "MP3",
            _ when codec.StartsWith("pcm_", StringComparison.Ordinal) => "LPCM",
            _ => codec.ToUpperInvariant(),
        };

        return ChannelToken(audio.Channels) is { } channels ? prefix + channels : prefix;
    }

    /// <summary>O DTS-HD MA vem do ffprobe como <c>dts</c>: quem o separa do DTS comum é o perfil.</summary>
    private static string DtsToken(string? profile) => profile switch
    {
        null => "DTS",
        _ when profile.Contains("DTS-HD MA", StringComparison.OrdinalIgnoreCase) => "DTS-HD.MA",
        _ when profile.Contains("DTS-HD HRA", StringComparison.OrdinalIgnoreCase) => "DTS-HD.HRA",
        _ when profile.Contains("DTS-ES", StringComparison.OrdinalIgnoreCase) => "DTS-ES",
        _ => "DTS",
    };

    private static string? ChannelToken(int? channels) => channels switch
    {
        1 => "1.0",
        2 => "2.0",
        6 => "5.1",
        7 => "6.1",
        8 => "7.1",
        _ => null,
    };

    private static string? Quality(MediaTrack? video) => video?.Height switch
    {
        >= 2160 => "2160p",
        >= 1080 => "1080p",
        >= 720 => "720p",
        >= 576 => "576p",
        >= 480 => "480p",
        _ => null,
    };

    private static string? VideoToken(MediaTrack? video) => video?.Codec.ToLowerInvariant() switch
    {
        "h264" => "AVC",
        "hevc" => "HEVC",
        "mpeg2video" => "MPEG2",
        "mpeg1video" => "MPEG1",
        "vc1" => "VC1",
        "av1" => "AV1",
        "vp9" => "VP9",
        null => null,
        var other => other.ToUpperInvariant(),
    };

    /// <summary>O título com os espaços e sinais trocados por ponto, como no nome de uma release.</summary>
    private static string Slug(string title)
    {
        var builder = new StringBuilder(title.Length);
        var separator = false;
        foreach (var character in title)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                separator = false;
            }
            else if (!separator && builder.Length > 0)
            {
                builder.Append('.');
                separator = true;
            }
        }

        return builder.ToString().Trim('.');
    }

    private static void Add(List<string> parts, string? part)
    {
        if (part is { Length: > 0 })
        {
            parts.Add(part);
        }
    }
}
