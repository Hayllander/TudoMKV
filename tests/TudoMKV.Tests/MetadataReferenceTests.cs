using TudoMKV.App.Integration;

namespace TudoMKV.Tests;

public class MetadataReferenceTests
{
    [Theory]
    [InlineData("tt0887736", MetadataSource.Imdb, "tt0887736")]
    [InlineData("https://www.imdb.com/title/tt0887736/", MetadataSource.Imdb, "tt0887736")]
    [InlineData("223883", MetadataSource.Tmdb, "223883")]
    [InlineData("  91970  ", MetadataSource.Tmdb, "91970")]
    [InlineData("https://www.themoviedb.org/movie/223883", MetadataSource.Tmdb, "223883")]
    [InlineData("https://www.themoviedb.org/movie/91970?language=pt-BR", MetadataSource.Tmdb, "91970")]
    [InlineData("https://anon.to/?https://www.themoviedb.org/movie/91970", MetadataSource.Tmdb, "91970")]
    public void SendsTheTypedReferenceToTheApiThatAnswersForIt(string input, MetadataSource source, string id)
    {
        var reference = MetadataReference.TryParse(input);

        Assert.NotNull(reference);
        Assert.Equal(source, reference.Source);
        Assert.Equal(id, reference.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dog and wolf")]
    [InlineData("tt12")]
    public void RefusesWhatIsNeitherAnImdbCodeNorATmdbNumber(string? input) =>
        Assert.Null(MetadataReference.TryParse(input));
}
