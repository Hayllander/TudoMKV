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
/// <param name="BitRateKbps">Bitrate pedido ao codificador; nulo nos codecs sem perda, que não usam bitrate.</param>
public sealed record AudioConversion(string Codec, int? BitRateKbps, string Filter)
{
    /// <summary>Codec FLAC, sem perda: a faixa gerada a partir do mono do Blu-ray e a conversão do PCM dele.</summary>
    public const string FlacCodec = "flac";

    /// <summary>Mono para estéreo 2.0 em AC-3, a mesma família do áudio de DVD.</summary>
    public static AudioConversion MonoToStereo { get; } = new("ac3", 192, "pan=stereo|c0=c0|c1=c0");

    /// <summary>
    /// Mono para estéreo 2.0 em FLAC, sem perda: é o que o Blu-ray pede, para a qualidade do áudio do
    /// disco não cair na faixa gerada.
    /// </summary>
    public static AudioConversion MonoToStereoFlac { get; } = new(FlacCodec, null, "pan=stereo|c0=c0|c1=c0");
}

/// <summary>
/// Codec para o qual a própria faixa é reencodada na gravação, sem filtro nenhum: a faixa de saída é a
/// mesma, com os mesmos canais, taxa de amostragem e profundidade.
/// </summary>
/// <param name="BitRateKbps">Bitrate pedido ao codificador; nulo nos codecs sem perda, que não usam bitrate.</param>
public sealed record AudioEncoding(string Codec, int? BitRateKbps)
{
    /// <summary>
    /// FLAC sem perda: é o destino do LPCM do Blu-ray, que sai com canais, taxa e profundidade como estão.
    /// </summary>
    public static AudioEncoding Flac { get; } = new(AudioConversion.FlacCodec, null);

    /// <summary>
    /// AC-3 a 192 kbps, o mesmo áudio que a faixa gerada do mono recebe: é o destino do PCM do DVD, que
    /// entraria no MKV com 1,5 Mb/s sem necessidade.
    /// </summary>
    public static AudioEncoding Ac3 { get; } = new("ac3", 192);
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

    /// <summary>
    /// Perfil do codec como o FFmpeg o reporta ("DTS-HD MA", "Dolby Digital Plus"...). É o que separa o
    /// DTS-HD MA - sem perda - do DTS comum, já que o ffprobe reporta os dois como <c>dts</c>.
    /// </summary>
    public string? Profile { get; init; }

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

    /// <summary>
    /// Bits por segundo do áudio: o que o ffprobe calcula ou o que as tags de estatística do arquivo
    /// (BPS, BITRATE) trazem, valendo o maior. Serve ao resumo por faixa, como no ShawScope-Audio.
    /// </summary>
    public long? BitRate { get; init; }

    /// <summary>Profundidade da amostra em bits, de bits_per_sample, bits_per_raw_sample ou do sample_fmt.</summary>
    public int? BitsPerSample { get; init; }

    /// <summary>Nome longo do codec ("DTS-HD Master Audio"), usado quando o codec não é conhecido.</summary>
    public string? CodecLongName { get; init; }

    /// <summary>Como esta faixa é gerada na gravação a partir de <see cref="ConvertedFrom"/>; nula nas faixas copiadas.</summary>
    public AudioConversion? Conversion { get; init; }

    /// <summary>A faixa da origem de que esta foi gerada (o áudio mono), para herdar idioma e nome.</summary>
    public MediaTrack? ConvertedFrom { get; init; }

    /// <summary>
    /// Codec para o qual a própria faixa é reencodada na gravação, sem filtro nenhum: o LPCM do Blu-ray vira
    /// FLAC e o PCM do DVD vira AC-3. Nulo quando a faixa é copiada como está.
    /// </summary>
    public AudioEncoding? EncodeTo { get; set; }

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
