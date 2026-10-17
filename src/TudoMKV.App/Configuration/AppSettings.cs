namespace TudoMKV.App.Configuration;

public sealed class AppSettings
{
    public IntegrationSettings Integration { get; set; } = new();
}

public sealed class IntegrationSettings
{
    public MetadataProvider Provider { get; set; } = MetadataProvider.None;

    /// <summary>Campo genérico para a credencial/dado principal da API escolhida.</summary>
    public string ApiData { get; set; } = string.Empty;
}

public enum MetadataProvider
{
    None,
    Omdb,
}
