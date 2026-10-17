namespace TudoMKV.App.Integration;

public sealed class OmdbMovieInfo
{
    public required string ImdbId { get; init; }

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

    public string PosterUrl { get; init; } = string.Empty;

    public string Plot { get; init; } = string.Empty;
}
