using TudoMKV.App.Configuration;

namespace TudoMKV.App.Integration;

/// <summary>
/// Busca a ficha do filme a partir do que foi digitado na tela: o número do filme ou o link do TMDb vai ao TMDb;
/// o código do IMDb vai ao OMDb, ou ao TMDb quando não há chave do OMDb. O TMDb é a fonte principal e o OMDb
/// cobre o que ele deixar faltando — a nota do IMDb, por exemplo — usando o código IMDb que o TMDb devolve.
/// </summary>
public sealed class MetadataLookup(OmdbMetadataService omdb, TmdbMetadataService tmdb)
{
    public async Task<MovieInfo> FetchAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (AppSettingsStore.Load().Integration.Provider != MetadataProvider.Tmdb)
        {
            throw new InvalidOperationException("Escolha a integração com o TMDb nas Preferências, na aba Integration.");
        }

        var parsed = MetadataReference.TryParse(reference)
            ?? throw new InvalidOperationException("Informe um código do IMDb (tt1234567), o número do filme no TMDb ou um link do IMDb/TMDb.");

        return parsed.Source == MetadataSource.Imdb
            ? await FetchFromImdbIdAsync(parsed.Id, cancellationToken)
            : await FetchFromTmdbAsync(parsed.Id, cancellationToken);
    }

    /// <summary>
    /// O código do IMDb vai ao OMDb quando há chave dele, como antes. Sem ela - o padrão, já que a chave do OMDb
    /// não vem preenchida - o /find do TMDb traduz o código para o número do filme lá.
    /// </summary>
    private async Task<MovieInfo> FetchFromImdbIdAsync(string imdbId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(AppSettingsStore.Load().Integration.ApiData))
        {
            return await omdb.FetchAsync(imdbId, cancellationToken);
        }

        var tmdbId = await tmdb.FindByImdbIdAsync(imdbId, cancellationToken)
            ?? throw new InvalidOperationException($"O TMDb não encontrou um filme com o código {imdbId}.");
        return await FetchFromTmdbAsync(tmdbId, cancellationToken);
    }

    private async Task<MovieInfo> FetchFromTmdbAsync(string tmdbId, CancellationToken cancellationToken)
    {
        var movie = await tmdb.FetchAsync(tmdbId, cancellationToken);

        // Só vale consultar a reserva quando falta o essencial e há um código IMDb para procurá-lo.
        if (movie.ImdbId.Length == 0 || (movie.Title.Length > 0 && movie.Plot.Length > 0))
        {
            return movie;
        }

        try
        {
            return Merge(movie, await omdb.FetchAsync(movie.ImdbId, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // O que o TMDb trouxe já basta: a reserva não pode derrubar a consulta.
            return movie;
        }
    }

    private static MovieInfo Merge(MovieInfo movie, MovieInfo fallback) => new()
    {
        TmdbId = movie.TmdbId,
        ImdbId = movie.ImdbId,
        Title = Prefer(movie.Title, fallback.Title),
        Year = Prefer(movie.Year, fallback.Year),
        Director = Prefer(movie.Director, fallback.Director),
        Genre = Prefer(movie.Genre, fallback.Genre),
        Country = Prefer(movie.Country, fallback.Country),
        Studio = Prefer(movie.Studio, fallback.Studio),
        Released = Prefer(movie.Released, fallback.Released),
        Runtime = Prefer(movie.Runtime, fallback.Runtime),
        Language = Prefer(movie.Language, fallback.Language),
        Actors = Prefer(movie.Actors, fallback.Actors),
        ImdbRating = Prefer(movie.ImdbRating, fallback.ImdbRating),
        TmdbRating = movie.TmdbRating,
        PosterUrl = Prefer(movie.PosterUrl, fallback.PosterUrl),
        Plot = Prefer(movie.Plot, fallback.Plot),
    };

    private static string Prefer(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
