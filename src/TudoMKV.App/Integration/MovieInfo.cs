namespace TudoMKV.App.Integration;

/// <summary>A ficha do filme montada a partir da API consultada, seja o TMDb ou o OMDb.</summary>
public sealed class MovieInfo
{
    /// <summary>Número do filme no TMDb, quando a consulta veio de lá.</summary>
    public string TmdbId { get; init; } = string.Empty;

    /// <summary>Código do IMDb: o informado na consulta ou o que o TMDb devolve no campo imdb_id.</summary>
    public string ImdbId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Year { get; init; } = string.Empty;

    public string Director { get; init; } = string.Empty;

    public string Genre { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;

    public string Studio { get; init; } = string.Empty;

    public string Released { get; init; } = string.Empty;

    public string Runtime { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;

    public string Actors { get; init; } = string.Empty;

    public string ImdbRating { get; init; } = string.Empty;

    /// <summary>Nota média do TMDb, de 0 a 10, quando a consulta veio de lá.</summary>
    public string TmdbRating { get; init; } = string.Empty;

    public string PosterUrl { get; init; } = string.Empty;

    public string Plot { get; init; } = string.Empty;
}
