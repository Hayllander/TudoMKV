using System.Xml.Linq;

namespace TudoMKV.App.Integration;

/// <summary>
/// Monta em memória as etiquetas globais do Matroska com o que a API do OMDb devolveu. Não existe mais um
/// tags.xml guardado na pasta do aplicativo: sem filme consultado não há etiquetas, e um disco nunca herda
/// os metadados do anterior.
/// </summary>
internal static class TagXml
{
    /// <summary>As etiquetas do filme consultado, prontas para o mkvpropedit.</summary>
    public static string Build(OmdbMovieInfo movie)
    {
        // Campo sem valor não é escrito de forma alguma (nem <String> vazio).
        var tag = new XElement("Tag",
            new XElement("Targets",
                new XElement("TargetTypeValue", "50"),
                new XComment(" 50 = arquivo inteiro ")));
        AddIfPresent(tag, "Title", movie.Title);
        AddIfPresent(tag, "Year", movie.Year);
        AddIfPresent(tag, "Director", movie.Director);
        AddIfPresent(tag, "Genre", movie.Genre);
        AddIfPresent(tag, "Country", movie.Country);
        AddIfPresent(tag, "Studio", movie.Studio);
        AddIfPresent(tag, "Description", movie.Plot);
        AddIfPresent(tag, "imdb", movie.ImdbId);

        return new XDocument(new XElement("Tags", tag)).ToString();
    }

    private static void AddIfPresent(XElement tag, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        tag.Add(new XElement("Simple",
            new XElement("Name", name),
            new XElement("String", value)));
    }
}
