using System.Text.Json;
using TudoMKV.App.Configuration;

namespace TudoMKV.App.Integration;

/// <summary>Consulta a ficha do filme no OMDb, que responde pelos códigos do IMDb.</summary>
public sealed class OmdbMetadataService
{
    private static readonly HttpClient Http = new();

    public async Task<MovieInfo> FetchAsync(string imdbId, CancellationToken cancellationToken = default)
    {
        var apiKey = AppSettingsStore.Load().Integration.ApiData;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Informe a chave da API OMDb nas Preferências, na aba Integration.");
        }

        var url = $"https://www.omdbapi.com/?i={Uri.EscapeDataString(imdbId)}&apikey={Uri.EscapeDataString(apiKey)}";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;

        if (!GetString(root, "Response").Equals("True", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(GetString(root, "Error") is { Length: > 0 } error
                ? $"OMDb: {error}"
                : "OMDb não encontrou informações para esse título.");
        }

        return new MovieInfo
        {
            ImdbId = imdbId,
            Title = Clean(GetString(root, "Title")),
            Year = Clean(GetString(root, "Year")),
            Director = Clean(GetString(root, "Director")),
            Genre = NormalizeGenre(Clean(GetString(root, "Genre"))),
            Country = Clean(GetString(root, "Country")),
            Studio = Clean(GetString(root, "Production")),
            Released = Clean(GetString(root, "Released")),
            Runtime = Clean(GetString(root, "Runtime")),
            Language = Clean(GetString(root, "Language")),
            Actors = Clean(GetString(root, "Actors")),
            ImdbRating = Clean(GetString(root, "imdbRating")),
            PosterUrl = Clean(GetString(root, "Poster")),
            Plot = Clean(GetString(root, "Plot")),
        };
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static string Clean(string value)
    {
        if (string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return value.Trim().Trim('`', '"', '\'');
    }

    private static string NormalizeGenre(string value) => value.Replace(", ", " | ", StringComparison.Ordinal);
}
