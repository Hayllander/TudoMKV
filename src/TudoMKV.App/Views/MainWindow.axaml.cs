using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TudoMKV.App.ViewModels;

namespace TudoMKV.App.Views;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType VideoFiles = new("Vídeos")
    {
        Patterns = ["*.mkv", "*.mp4", "*.m4v", "*.mov", "*.m2ts", "*.mts", "*.ts", "*.vob", "*.avi", "*.webm", "*.mpg", "*.mpeg"],
    };

    private static readonly FilePickerFileType IsoFiles = new("Imagens de DVD")
    {
        Patterns = ["*.iso"],
    };

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    private async void OnAddFilesClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Adicionar arquivos",
            AllowMultiple = true,
            FileTypeFilter = [VideoFiles, FilePickerFileTypes.All],
        });

        await ViewModel.AddPathsAsync(LocalPaths(files));
    }

    /// <summary>Monta o menu de DVD na hora, para refletir os discos inseridos agora.</summary>
    private void OnDiscMenuOpening(object? sender, EventArgs e)
    {
        var menu = (MenuFlyout)sender!;
        menu.Items.Clear();

        foreach (var drive in OpticalDrives())
        {
            var root = drive.RootDirectory.FullName;
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? root : $"{drive.VolumeLabel} ({root})";
            var item = new MenuItem { Header = $"Drive {label}" };
            item.Click += async (_, _) => await ViewModel.AddPathsAsync([root]);
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "Nenhum disco no drive", IsEnabled = false });
        }

        menu.Items.Add(new Separator());

        var folder = new MenuItem { Header = "Pasta de DVD (VIDEO_TS)…" };
        folder.Click += OnOpenDvdFolderClick;
        menu.Items.Add(folder);

        var iso = new MenuItem { Header = "Imagem ISO…" };
        iso.Click += OnOpenIsoClick;
        menu.Items.Add(iso);
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

    private async void OnOpenDvdFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pasta do DVD (a que contém VIDEO_TS, ou a própria VIDEO_TS)",
        });

        await ViewModel.AddPathsAsync(folders.Select(f => f.TryGetLocalPath()).OfType<string>().ToList());
    }

    private async void OnOpenIsoClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Imagem ISO de DVD",
            AllowMultiple = true,
            FileTypeFilter = [IsoFiles],
        });

        await ViewModel.AddPathsAsync(LocalPaths(files));
    }

    private async void OnChooseOutputClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pasta de saída",
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            ViewModel.OutputDirectory = path;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    // Aceita arquivos de vídeo, ISOs e pastas de DVD.
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is { } items)
        {
            await ViewModel.AddPathsAsync(items.Select(i => i.TryGetLocalPath()).OfType<string>().ToList());
        }
    }

    private static List<string> LocalPaths(IEnumerable<IStorageFile> files) =>
        files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
}
