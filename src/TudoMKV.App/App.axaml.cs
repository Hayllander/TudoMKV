using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TudoMKV.App.ViewModels;
using TudoMKV.App.Views;
using TudoMKV.Muxing;

namespace TudoMKV.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel(new FfprobeInspector(), new FfmpegMuxer());
            var window = new MainWindow { DataContext = viewModel };

            // Arquivos passados na linha de comando (ex.: "Abrir com" no Explorer) entram na lista.
            if (desktop.Args is { Length: > 0 } files)
            {
                window.Opened += async (_, _) => await viewModel.AddFilesAsync(files);
            }

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
