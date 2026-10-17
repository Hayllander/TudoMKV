using Avalonia.Media.Imaging;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TudoMKV.App.Integration;
using TudoMKV.App.ViewModels;

namespace TudoMKV.App.Views;

public partial class ImdbInfoWindow : Window
{
    private static readonly HttpClient Http = new();
    private readonly OmdbMovieInfo? _movie;

    public ImdbInfoWindow()
    {
        InitializeComponent();
    }

    public ImdbInfoWindow(OmdbMovieInfo movie)
        : this()
    {
        _movie = movie;
        DataContext = new ImdbInfoDialogViewModel(movie);
        Opened += OnOpened;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        await LoadPosterAsync();
    }

    private async Task LoadPosterAsync()
    {
        if (_movie is null || string.IsNullOrWhiteSpace(_movie.PosterUrl))
        {
            PosterPlaceholder.Text = "Poster indisponível";
            return;
        }

        try
        {
            PosterPlaceholder.Text = "Carregando poster...";
            var bytes = await Http.GetByteArrayAsync(_movie.PosterUrl);
            await using var stream = new MemoryStream(bytes, writable: false);
            PosterImage.Source = new Bitmap(stream);
            PosterImage.IsVisible = true;
            PosterPlaceholder.IsVisible = false;
        }
        catch
        {
            PosterPlaceholder.Text = "Poster indisponível";
        }
    }
}
