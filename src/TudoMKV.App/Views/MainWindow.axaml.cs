using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using TudoMKV.App.Integration;
using TudoMKV.App.ViewModels;

namespace TudoMKV.App.Views;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType DiscEntryFiles = new("Arquivos de disco")
    {
        Patterns =
        [
            "VIDEO_TS.IFO", "VIDEO_TS.ifo",
            "index.bdmv", "INDEX.BDMV",
            "MovieObject.bdmv", "MOVIEOBJECT.BDMV",
            "*.iso", "*.ISO", "*.img", "*.IMG",
        ],
    };

    private static readonly FilePickerFileType VideoFiles = new("Arquivos de vídeo")
    {
        Patterns =
        [
            "*.mkv", "*.mp4", "*.m4v", "*.mov", "*.m2ts", "*.mts", "*.ts", "*.vob", "*.avi", "*.webm", "*.mpg", "*.mpeg",
        ],
    };

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        ActualThemeVariantChanged += (_, _) => UpdateThemeIcon();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.Log.CollectionChanged += OnLogChanged;
            }
        };
        UpdateThemeIcon();
        Opened += OnWindowOpened;
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    /// <summary>Primeira linha do log: versão em uso e, quando houver, o aviso de versão mais nova.</summary>
    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        Opened -= OnWindowOpened;
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.StartupAsync();
        }
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(() =>
            {
                LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
            }, DispatcherPriority.Background);
        }
    }

    private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir: VIDEO_TS.IFO, index.bdmv, MovieObject.bdmv, ISO, IMG ou vídeos",
            AllowMultiple = true,
            FileTypeFilter = [DiscEntryFiles, VideoFiles, FilePickerFileTypes.All],
        });

        await ViewModel.OpenAsync(LocalPaths(files));
    }

    private async void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pasta do disco (a que contém VIDEO_TS ou BDMV)",
        });

        await ViewModel.OpenAsync(LocalPaths(folders));
    }

    private void OnDiscMenuOpened(object? sender, RoutedEventArgs e) => FillDriveItems(DiscMenu.Items);

    private void OnDiscFlyoutOpening(object? sender, EventArgs e) => FillDriveItems(((MenuFlyout)sender!).Items);

    /// <summary>Lista os drives ópticos com disco na hora em que o menu abre.</summary>
    private void FillDriveItems(ItemCollection items)
    {
        items.Clear();
        foreach (var drive in OpticalDrives())
        {
            var root = drive.RootDirectory.FullName;
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? root : $"{root}  {drive.VolumeLabel}";
            var item = new MenuItem { Header = label };
            item.Click += async (_, _) => await ViewModel.OpenAsync([root]);
            items.Add(item);
        }

        if (items.Count == 0)
        {
            items.Add(new MenuItem { Header = "Nenhum disco encontrado no drive", IsEnabled = false });
        }
    }

    private static IEnumerable<DriveInfo> OpticalDrives()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            bool ready;
            try
            {
                ready = drive.DriveType == DriveType.CDRom && drive.IsReady;
            }
            catch (IOException)
            {
                ready = false;
            }

            if (ready)
            {
                yield return drive;
            }
        }
    }

    private async void OnChooseOutputClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pasta de saída" });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            ViewModel.OutputDirectory = path;
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private void OnToggleThemeClick(object? sender, RoutedEventArgs e)
    {
        RequestedThemeVariant = ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        UpdateThemeIcon();
    }

    /// <summary>No tema escuro o botão mostra o sol (ir para o claro); no claro, a lua.</summary>
    private void UpdateThemeIcon()
    {
        var key = ActualThemeVariant == ThemeVariant.Dark ? "IconSun" : "IconMoon";
        if (this.TryFindResource(key, out var icon) && icon is StreamGeometry geometry)
        {
            ThemeIcon.Data = geometry;
        }
    }

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";
        var dialog = new Window
        {
            Title = "Sobre o TudoMKV",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock
            {
                Margin = new Avalonia.Thickness(20),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Text = $"TudoMKV {version}\n\nGrava DVDs, Blu-rays e arquivos de vídeo em MKV sem recodificar, usando o FFmpeg.\n\n" +
                       "Não contorna proteções contra cópia (CSS, AACS).\n\nLicença GPLv3.",
            },
        };
        await dialog.ShowDialog(this);
    }

    private async void OnPreferencesClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new PreferencesWindow();
        await dialog.ShowDialog<bool?>(this);
    }

    private async void OnVerifyMetadataClick(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.VerifyMetadataCommand.CanExecute(null))
        {
            return;
        }

        await ViewModel.VerifyMetadataCommand.ExecuteAsync(null);
        if (ViewModel.LastVerifiedMovie is not { } movie)
        {
            return;
        }

        var dialog = new MovieInfoWindow(movie);
        await dialog.ShowDialog(this);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is { } items)
        {
            await ViewModel.OpenAsync(LocalPaths(items));
        }
    }

    private static List<string> LocalPaths(IEnumerable<IStorageItem> items) =>
        items.Select(i => i.TryGetLocalPath()).OfType<string>().ToList();
}
