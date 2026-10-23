using System.Globalization;
using System.Text.Json;
using TudoMKV.App.Configuration;

namespace TudoMKV.App.Integration;

/// <summary>
/// Consulta a ficha do filme no TMDb, a fonte principal: responde pelo número do filme ou pelo link dele.
/// Os detalhes vêm com os créditos anexados de uma vez, para o diretor e o elenco saírem da mesma chamada.
/// </summary>
public sealed class TmdbMetadataService
{
    private const string Language = "en-US";
    private const string ImageBaseUrl = "https://image.tmdb.org/t/p/w500";
    private const int ActorCount = 5;

    private static readonly HttpClient Http = new();

    /// <summary>
    /// Número do filme no TMDb a partir do código do IMDb, pelo /find. Nulo quando o TMDb não conhece o código
    /// ou o código é de uma série, e não de um filme.
    /// </summary>
    public async Task<string?> FindByImdbIdAsync(string imdbId, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.themoviedb.org/3/find/{Uri.EscapeDataString(imdbId)}" +
                  $"?api_key={Uri.EscapeDataString(ApiKey())}&external_source=imdb_id";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("movie_results", out var movies) || movies.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return movies.EnumerateArray()
            .Select(movie => movie.TryGetProperty("id", out var id) && id.TryGetInt32(out var number) ? number : 0)
            .Where(number => number > 0)
            .Select(number => number.ToString(CultureInfo.InvariantCulture))
            .FirstOrDefault();
    }

    public async Task<MovieInfo> FetchAsync(string tmdbId, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.themoviedb.org/3/movie/{Uri.EscapeDataString(tmdbId)}" +
                  $"?api_key={Uri.EscapeDataString(ApiKey())}&language={Language}&append_to_response=credits";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;

        // Filme inexistente vem com status_code em vez de id; a mensagem dele é a que explica o erro.
        if (root.TryGetProperty("status_code", out _))
        {
            throw new InvalidOperationException(GetString(root, "status_message") is { Length: > 0 } message
                ? $"TMDb: {message}"
                : "TMDb não encontrou informações para esse título.");
        }

        return new MovieInfo
        {
            TmdbId = tmdbId,
            ImdbId = Clean(GetString(root, "imdb_id")),
            Title = Clean(GetString(root, "title")) is { Length: > 0 } title
                ? title
                : Clean(GetString(root, "original_title")),
            Year = ReleaseYear(root),
            Director = Director(root),
            Genre = Names(root, "genres", "name", " | "),
            Country = Names(root, "production_countries", "name", ", "),
            Studio = Names(root, "production_companies", "name", ", "),
            Released = ReleaseDate(root),
            Runtime = Minutes(root),
            Language = Names(root, "spoken_languages", "english_name", ", "),
            Actors = Actors(root),
            TmdbRating = Rating(root),
            PosterUrl = Poster(root),
            Plot = Clean(GetString(root, "overview")),
        };
    }

    private static string ApiKey()
    {
        var apiKey = AppSettingsStore.Load().Integration.TmdbApiKey;
        return string.IsNullOrWhiteSpace(apiKey)
            ? throw new InvalidOperationException("Informe a chave da API TMDb nas Preferências, na aba Integration.")
            : apiKey;
    }

    private static string Director(JsonElement root)
    {
        if (!root.TryGetProperty("credits", out var credits) || !credits.TryGetProperty("crew", out var crew))
        {
            return string.Empty;
        }

        return string.Join(", ", crew.EnumerateArray()
            .Where(person => GetString(person, "job").Equals("Director", StringComparison.OrdinalIgnoreCase))
            .Select(person => Clean(GetString(person, "name")))
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string Actors(JsonElement root)
    {
        if (!root.TryGetProperty("credits", out var credits) || !credits.TryGetProperty("cast", out var cast))
        {
            return string.Empty;
        }

        return string.Join(", ", cast.EnumerateArray()
            .Take(ActorCount)
            .Select(person => Clean(GetString(person, "name")))
            .Where(name => name.Length > 0));
    }

    private static string Names(JsonElement root, string arrayName, string fieldName, string separator)
    {
        if (!root.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(separator, array.EnumerateArray()
            .Select(item => Clean(GetString(item, fieldName)))
            .Where(name => name.Length > 0));
    }

    private static string ReleaseYear(JsonElement root) =>
        Clean(GetString(root, "release_date")) is { Length: >= 4 } date ? date[..4] : string.Empty;

    /// <summary>A data sai como no OMDb ("7 Jun 1973"), para a ficha ficar igual nas duas fontes.</summary>
    private static string ReleaseDate(JsonElement root)
    {
        var date = Clean(GetString(root, "release_date"));
        return DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : date;
    }

    private static string Minutes(JsonElement root) =>
        root.TryGetProperty("runtime", out var runtime) && runtime.TryGetInt32(out var minutes) && minutes > 0
            ? $"{minutes} min"
            : string.Empty;

    /// <summary>A nota do TMDb só aparece quando houve voto: filme sem votação traz média zerada.</summary>
    private static string Rating(JsonElement root)
    {
        if (!root.TryGetProperty("vote_count", out var count) || !count.TryGetInt32(out var votes) || votes == 0)
        {
            return string.Empty;
        }

        return root.TryGetProperty("vote_average", out var average) && average.TryGetDouble(out var value)
            ? value.ToString("0.0", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string Poster(JsonElement root) =>
        Clean(GetString(root, "poster_path")) is { Length: > 0 } path ? ImageBaseUrl + path : string.Empty;

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static string Clean(string value) => value.Trim().Trim('`', '"', '\'');
}
