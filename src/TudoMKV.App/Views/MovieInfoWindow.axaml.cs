using Avalonia.Media.Imaging;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TudoMKV.App.Integration;
using TudoMKV.App.ViewModels;

namespace TudoMKV.App.Views;

public partial class MovieInfoWindow : Window
{
    private static readonly HttpClient Http = new();
    private readonly MovieInfo? _movie;

    public MovieInfoWindow()
    {
        InitializeComponent();
    }

    public MovieInfoWindow(MovieInfo movie)
        : this()
    {
        _movie = movie;
        DataContext = new MovieInfoDialogViewModel(movie);
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
