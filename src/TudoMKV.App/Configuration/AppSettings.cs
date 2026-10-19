namespace TudoMKV.App.Configuration;

public sealed class AppSettings
{
    public IntegrationSettings Integration { get; set; } = new();

    public MuxingSettings Muxing { get; set; } = new();
}

public sealed class MuxingSettings
{
    /// <summary>
    /// Marca o MKV de vídeo MPEG-2 com <c>MuxingApp=libmakemkv</c>: sem essa marca o VLC 3 mostra imagem
    /// verde ao avançar com decodificação por hardware. Ligada por padrão.
    /// </summary>
    public bool MarkMpeg2ForVlc { get; set; } = true;

    /// <summary>
    /// Assinatura no fim do nome do MKV, como nas releases de cena (o "-HAY" de
    /// <c>Painted.Skin.2008.1080p.BluRay.REMUX.AVC.DTS-HD.MA7.1-HAY</c>).
    /// </summary>
    public string ReleaseGroup { get; set; } = "HAY";
}

public sealed class IntegrationSettings
{
    /// <summary>Chave padrão do TMDb, para o aplicativo já funcionar sem configuração.</summary>
    public const string DefaultTmdbApiKey = "9389a80c739dffb67fe5200278d33ab2";

    public MetadataProvider Provider { get; set; } = MetadataProvider.Tmdb;

    /// <summary>Chave do TMDb, a fonte principal: responde pelo número ou pelo link do filme.</summary>
    public string TmdbApiKey { get; set; } = DefaultTmdbApiKey;

    /// <summary>
    /// Chave do OMDb: responde pelos códigos IMDb e completa o que o TMDb não trouxer. O nome do campo é o mesmo
    /// gravado pelas versões anteriores, para a chave já configurada continuar valendo.
    /// </summary>
    public string ApiData { get; set; } = string.Empty;
}

public enum MetadataProvider
{
    None = 0,

    /// <summary>
    /// TMDb como fonte principal, com o OMDb cobrindo os códigos IMDb. Nas versões anteriores este valor (1)
    /// significava OMDb, de modo que uma configuração antiga passa a usar o TMDb.
    /// </summary>
    Tmdb = 1,
}
