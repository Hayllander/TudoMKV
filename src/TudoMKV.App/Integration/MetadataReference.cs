using System.Text.RegularExpressions;

namespace TudoMKV.App.Integration;

/// <summary>A API que responde por uma referência digitada na tela.</summary>
public enum MetadataSource
{
    /// <summary>Código do IMDb: quem responde é o OMDb.</summary>
    Imdb,

    /// <summary>Número ou link do TMDb: quem responde é o TMDb.</summary>
    Tmdb,
}

/// <summary>A referência de filme digitada, já resolvida para a API que responde por ela.</summary>
public sealed record MetadataReference(MetadataSource Source, string Id)
{
    private static readonly Regex ImdbIdRegex = new(@"tt\d{7,9}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TmdbMovieRegex = new(@"/movie/(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TmdbNumberRegex = new(@"^\d+$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Aceita o código do IMDb (tt1234567), o link do IMDb, o número do TMDb ou o link do TMDb — inclusive
    /// embrulhado em outro endereço, como em https://anon.to/?https://www.themoviedb.org/movie/91970.
    /// </summary>
    public static MetadataReference? TryParse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim();

        if (ImdbIdRegex.Match(text) is { Success: true } imdb)
        {
            return new MetadataReference(MetadataSource.Imdb, imdb.Value.ToLowerInvariant());
        }

        if (TmdbMovieRegex.Match(text) is { Success: true } movie)
        {
            return new MetadataReference(MetadataSource.Tmdb, movie.Groups[1].Value);
        }

        return TmdbNumberRegex.IsMatch(text) ? new MetadataReference(MetadataSource.Tmdb, text) : null;
    }
}
