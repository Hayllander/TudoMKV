using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TudoMKV.App.ViewModels;

namespace TudoMKV.App.Views;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType DiscAndVideoFiles = new("Discos e vídeos")
    {
        Patterns =
        [
            "*.IFO", "*.ifo", "*.bdmv", "*.BDMV", "*.iso", "*.ISO", "*.img", "*.IMG",
            "*.mkv", "*.mp4", "*.m4v", "*.mov", "*.m2ts", "*.mts", "*.ts", "*.vob", "*.avi", "*.webm", "*.mpg", "*.mpeg",
        ],
    };

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.Log.CollectionChanged += OnLogChanged;
            }
        };
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

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
            Title = "Abrir: VIDEO_TS.IFO, index.bdmv, MovieObject.bdmv, ISO ou vídeos",
            AllowMultiple = true,
            FileTypeFilter = [DiscAndVideoFiles, FilePickerFileTypes.All],
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
