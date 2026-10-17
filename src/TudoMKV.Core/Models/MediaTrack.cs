namespace TudoMKV.Core.Models;

public enum TrackType
{
    Video,
    Audio,
    Subtitle,
    Attachment,
    Data,
}

/// <summary>
/// Como uma faixa gerada na gravação é produzida a partir da faixa de origem. O grafo copia o canal
/// mono nos dois canais em nível unitário; o <c>-ac 2</c> sozinho aplicaria -3 dB (upmix por potência)
/// e a faixa estéreo sairia mais baixa que a mono.
/// </summary>
public sealed record AudioConversion(string Codec, int BitRateKbps, string Filter)
{
    /// <summary>Mono para estéreo 2.0 em AC-3, a mesma família do áudio de DVD.</summary>
    public static AudioConversion MonoToStereo { get; } = new("ac3", 192, "pan=stereo|c0=c0|c1=c0");
}

/// <summary>Uma faixa (stream) de um arquivo ou título de disco.</summary>
public sealed class MediaTrack
{
    /// <summary>Índice da entrada do ffmpeg a que esta faixa pertence.</summary>
    public int InputIndex { get; init; }

    /// <summary>Índice do stream na origem, como o FFmpeg o numera.</summary>
    public required int Index { get; init; }

    /// <summary>Identificador do stream no contêiner (PID no Blu-ray), quando existe.</summary>
    public int? StreamId { get; init; }

    /// <summary>
    /// Identificador da faixa no disco de origem no formato da tag SOURCE_ID do MakeMKV
    /// ("00" + PID no Blu-ray, "01" + subfluxo + fluxo no DVD), que o MediaInfo mostra como origem.
    /// </summary>
    public string? SourceId { get; set; }

    public required TrackType Type { get; init; }

    /// <summary>Nome do codec como o FFmpeg o reporta (h264, ac3, hdmv_pgs_subtitle...).</summary>
    public required string Codec { get; init; }

    /// <summary>Código de idioma ISO 639-2 (por, eng...) ou "und".</summary>
    public string Language { get; set; } = "und";

    public string? Title { get; set; }

    public bool IsDefault { get; set; }

    public bool IsForced { get; set; }

    /// <summary>Se a faixa entra no MKV de saída.</summary>
    public bool Selected { get; set; } = true;

    public int? Width { get; init; }

    public int? Height { get; init; }

    public int? Channels { get; init; }

    public int? SampleRate { get; init; }

    /// <summary>Como esta faixa é gerada na gravação a partir de <see cref="ConvertedFrom"/>; nula nas faixas copiadas.</summary>
    public AudioConversion? Conversion { get; init; }

    /// <summary>A faixa da origem de que esta foi gerada (o áudio mono), para herdar idioma e nome.</summary>
    public MediaTrack? ConvertedFrom { get; init; }

    /// <summary>Faixa criada pelo TudoMKV na gravação, e não presente na origem.</summary>
    public bool IsGenerated => Conversion is not null;

    /// <summary>Faixas que o Matroska não aceita (ex.: timecode de MP4) não podem ser copiadas.</summary>
    public bool CanMux => Type is not TrackType.Data;

    public string Summary => Type switch
    {
        TrackType.Video when Width is not null => $"{Codec} {Width}x{Height}",
        TrackType.Audio when Channels is not null => $"{Codec} {ChannelLayoutName(Channels.Value)}",
        _ => Codec,
    };

    private static string ChannelLayoutName(int channels) => channels switch
    {
        1 => "mono",
        2 => "estéreo",
        6 => "5.1",
        8 => "7.1",
        _ => $"{channels} canais",
    };
}
