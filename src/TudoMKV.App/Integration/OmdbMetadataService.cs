using System.Text.Json;
using System.Text.RegularExpressions;
using TudoMKV.App.Configuration;

namespace TudoMKV.App.Integration;

public sealed class OmdbMetadataService
{
    private static readonly HttpClient Http = new();
    private static readonly Regex ImdbIdRegex = new(@"tt\d{7,9}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? TryExtractImdbId(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var match = ImdbIdRegex.Match(input);
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    public async Task<OmdbMovieInfo> FetchAsync(string imdbReference, CancellationToken cancellationToken = default)
    {
        var settings = AppSettingsStore.Load();
        if (settings.Integration.Provider != MetadataProvider.Omdb)
        {
            throw new InvalidOperationException("Selecione OMDb nas Preferências, na aba Integration.");
        }

        if (string.IsNullOrWhiteSpace(settings.Integration.ApiData))
        {
            throw new InvalidOperationException("Informe a chave da API OMDb nas Preferências, na aba Integration.");
        }

        var imdbId = TryExtractImdbId(imdbReference)
            ?? throw new InvalidOperationException("Informe um link do IMDb ou um código no formato tt1234567.");

        var url = $"https://www.omdbapi.com/?i={imdbId}&apikey={Uri.EscapeDataString(settings.Integration.ApiData)}";
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

        return new OmdbMovieInfo
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
