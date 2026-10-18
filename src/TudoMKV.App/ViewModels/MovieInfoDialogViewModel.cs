using TudoMKV.App.Integration;

namespace TudoMKV.App.ViewModels;

public sealed class MovieInfoDialogViewModel
{
    public MovieInfoDialogViewModel(MovieInfo movie)
    {
        Title = string.IsNullOrWhiteSpace(movie.Year)
            ? movie.Title
            : $"{movie.Title} ({movie.Year})";
        Subtitle = movie.TmdbId.Length > 0 ? $"TMDb: {movie.TmdbId}" : $"IMDb: {movie.ImdbId}";
        Description = movie.Plot;
        Rows = CreateRows(movie);
    }

    public string Title { get; }

    public string Subtitle { get; }

    public string Description { get; }

    public IReadOnlyList<InfoRow> Rows { get; }

    private static IReadOnlyList<InfoRow> CreateRows(MovieInfo movie)
    {
        var rows = new List<InfoRow>();
        AddIfPresent(rows, "Ano", movie.Year);
        AddIfPresent(rows, "Diretor", movie.Director);
        AddIfPresent(rows, "Genero", movie.Genre);
        AddIfPresent(rows, "Pais", movie.Country);
        AddIfPresent(rows, "Estudio", movie.Studio);
        AddIfPresent(rows, "Lancamento", movie.Released);
        AddIfPresent(rows, "Duracao", movie.Runtime);
        AddIfPresent(rows, "Idioma", movie.Language);
        AddIfPresent(rows, "Atores", movie.Actors);
        AddIfPresent(rows, "Nota IMDb", movie.ImdbRating);
        AddIfPresent(rows, "Nota TMDb", movie.TmdbRating);
        AddIfPresent(rows, "Codigo IMDb", movie.ImdbId);
        AddIfPresent(rows, "Codigo TMDb", movie.TmdbId);
        return rows;
    }

    private static void AddIfPresent(ICollection<InfoRow> rows, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            rows.Add(new InfoRow(name, value));
        }
    }
}
