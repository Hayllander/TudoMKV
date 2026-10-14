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

        await ViewModel.AddFilesAsync(LocalPaths(files));
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

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is { } items)
        {
            await ViewModel.AddFilesAsync(LocalPaths(items.OfType<IStorageFile>()));
        }
    }

    private static IEnumerable<string> LocalPaths(IEnumerable<IStorageFile> files) =>
        files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
}
