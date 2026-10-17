using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TudoMKV.App.Integration;
using TudoMKV.App.ViewModels;
using TudoMKV.App.Views;
using TudoMKV.Disc.Dvd;
using TudoMKV.Muxing;

namespace TudoMKV.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var inspector = new FfprobeInspector();
            var viewModel = new MainWindowViewModel(
                new SourceOpener(inspector),
                new FfmpegMuxer(),
                new SelectionMuxJobBuilder(inspector),
                new OmdbMetadataService(),
                new TagFileService(),
                new MkvTagWriter());
            var window = new MainWindow { DataContext = viewModel };

            // Caminho passado na linha de comando (ex.: "Abrir com" no Explorer) já abre.
            if (desktop.Args is { Length: > 0 } files)
            {
                window.Opened += async (_, _) => await viewModel.OpenAsync(files);
            }

            desktop.MainWindow = window;
            desktop.Exit += (_, _) => IsoMount.ReleaseAll();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
