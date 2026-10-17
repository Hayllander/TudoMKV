using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TudoMKV.App.Integration;

public sealed class TagFileService
{
    public string TagsPath => ResolveTagsPath();

    public void UpdateFromOmdb(OmdbMovieInfo movie)
    {
        // O arquivo é reescrito do zero a cada consulta: só entra o que veio da API, sem sobra
        // de outro filme. Campo sem valor não é escrito de forma alguma (nem <String> vazio).
        var tag = CreateTag();
        AddIfPresent(tag, "Title", movie.Title);
        AddIfPresent(tag, "Year", movie.Year);
        AddIfPresent(tag, "Director", movie.Director);
        AddIfPresent(tag, "Genre", movie.Genre);
        AddIfPresent(tag, "Country", movie.Country);
        AddIfPresent(tag, "Studio", movie.Studio);
        AddIfPresent(tag, "Description", movie.Plot);
        AddIfPresent(tag, "imdb", movie.ImdbId);

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("Tags", tag));

        var path = TagsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Escreve em um temporário e substitui no fim: uma falha no meio não trunca o tags.xml.
        var tempPath = path + ".tmp";
        using (var writer = XmlWriter.Create(tempPath, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false,
        }))
        {
            document.Save(writer);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private static XElement CreateTag() =>
        new("Tag",
            new XElement("Targets",
                new XElement("TargetTypeValue", "50"),
                new XComment(" 50 = arquivo inteiro ")));

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

    private static string ResolveTagsPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tags.xml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "tags.xml");
    }
}
