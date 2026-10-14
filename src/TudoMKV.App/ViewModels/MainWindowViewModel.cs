using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TudoMKV.Core;
using TudoMKV.Core.Models;
using TudoMKV.Muxing;

namespace TudoMKV.App.ViewModels;

/// <summary>
/// Tela única no estilo MakeMKV: abre uma origem, mostra a árvore Disco &gt; Título &gt; Faixas,
/// e grava os títulos marcados na pasta de saída.
/// </summary>
public sealed partial class MainWindowViewModel(SourceOpener opener, IMuxer muxer) : ObservableObject
{
    private readonly List<(MediaSource Source, TreeNodeViewModel Title, TreeNodeViewModel? Chapters)> _titles = [];
    private CancellationTokenSource? _cancellation;

    /// <summary>Títulos mais curtos que isto são pulados (menus, avisos, trailers), como no MakeMKV.</summary>
    public TimeSpan MinimumTitleLength { get; set; } = TimeSpan.FromSeconds(120);

    public ObservableCollection<TreeNodeViewModel> Nodes { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InfoText))]
    public partial TreeNodeViewModel? SelectedNode { get; set; }

    public string InfoText => SelectedNode?.Info
        ?? "Abra um disco ou arquivo pelo menu Arquivo, pelos botões acima ou arrastando para esta janela.\n\n" +
           "DVD: escolha o VIDEO_TS.IFO (ou a pasta VIDEO_TS).\n" +
           "Blu-ray: escolha o index.bdmv ou o MovieObject.bdmv dentro da pasta BDMV.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MakeMkvCommand))]
    public partial string? OutputDirectory { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MakeMkvCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    public partial bool IsRipping { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Pronto";

    public async Task OpenAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Lendo…";
        try
        {
            var contents = await opener.OpenAsync(paths, MinimumTitleLength);
            foreach (var message in contents.Messages)
            {
                AddLog(message);
            }

            Show(contents);
            OutputDirectory = DefaultOutputDirectory(contents, paths);
            AddLog(contents.Titles.Count > 0 ? "Operação finalizada com sucesso" : "Nenhum título encontrado");
        }
        catch (Exception ex) when (ex is MuxException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AddLog($"Falha ao abrir: {ex.Message}");
            if (ex is MuxException { Log: { Length: > 0 } log })
            {
                AddLog(log);
            }
        }
        finally
        {
            IsBusy = false;
            StatusText = "Pronto";
            MakeMkvCommand.NotifyCanExecuteChanged();
        }
    }

    private void Show(DiscContents contents)
    {
        Nodes.Clear();
        _titles.Clear();

        var root = new TreeNodeViewModel(
            NodeKind.Source, 0, Describe.KindName(contents.Kind), contents.Label,
            $"Informações da Origem\nTipo: {Describe.KindName(contents.Kind)}\nNome: {contents.Label}\nTítulos: {contents.Titles.Count}");

        foreach (var source in contents.Titles)
        {
            var label = contents.Kind == SourceKind.Files ? source.DisplayName : contents.Label;
            var title = new TreeNodeViewModel(
                NodeKind.Title, 1, "Título", Describe.Title(source, label), TitleInfo(source),
                isChecked: true, onChecked: _ => MakeMkvCommand.NotifyCanExecuteChanged())
            {
                Title = source,
            };

            foreach (var track in source.Tracks.Where(t => t.CanMux))
            {
                title.Children.Add(new TreeNodeViewModel(
                    NodeKind.Track, 2, Describe.TrackTypeName(track.Type), Describe.Track(track), Describe.TrackInfo(track),
                    isChecked: track.Selected, onChecked: value => track.Selected = value)
                {
                    Title = source,
                });
            }

            TreeNodeViewModel? chapters = null;
            if (source.ChapterCount > 0)
            {
                chapters = new TreeNodeViewModel(
                    NodeKind.Chapters, 2, "Capítulos", $"{source.ChapterCount} capítulo(s)",
                    $"Capítulos\nO MKV leva {source.ChapterCount} marcadores de capítulo.", isChecked: true)
                {
                    Title = source,
                };
                title.Children.Add(chapters);
            }

            root.Children.Add(title);
            _titles.Add((source, title, chapters));
        }

        Nodes.Add(root);
        SelectedNode = root;
    }

    private static string TitleInfo(MediaSource source)
    {
        var lines = new List<string>
        {
            "Informações do Título",
            $"Nome: {source.DisplayName}",
            $"Duração: {Describe.Duration(source.Duration)}",
        };
        if (source.ChapterCount > 0)
        {
            lines.Add($"Capítulos: {source.ChapterCount}");
        }

        if (Describe.Size(source.SizeBytes) is { Length: > 0 } size)
        {
            lines.Add($"Tamanho: {size}");
        }

        lines.Add($"Faixas: {source.Tracks.Count(t => t.CanMux)}");
        lines.Add($"Arquivo de saída: {(source.OutputBaseName ?? Path.GetFileNameWithoutExtension(source.Path))}.mkv");
        return string.Join('\n', lines);
    }

    private static string? DefaultOutputDirectory(DiscContents contents, IReadOnlyList<string> paths)
    {
        if (contents.Kind == SourceKind.Files)
        {
            return Path.GetDirectoryName(Path.GetFullPath(paths[0]));
        }

        var videos = Path.GetDirectoryName(MuxJob.DefaultDiscOutputDirectory)!;
        var folder = string.Join("_", contents.Label.Split(Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToArray(), StringSplitOptions.TrimEntries));
        return Path.Combine(videos, folder);
    }

    private bool CanMakeMkv() =>
        !IsBusy && !string.IsNullOrWhiteSpace(OutputDirectory) && _titles.Any(t => t.Title.IsChecked);

    [RelayCommand(CanExecute = nameof(CanMakeMkv))]
    private async Task MakeMkvAsync()
    {
        var selected = _titles.Where(t => t.Title.IsChecked).ToList();
        IsBusy = true;
        IsRipping = true;
        _cancellation = new CancellationTokenSource();
        var done = 0;
        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                var (source, _, chapters) = selected[i];
                var job = MuxJob.FromSelection(source, OutputDirectory, keepChapters: chapters?.IsChecked ?? true);
                if (job.Tracks.Count == 0)
                {
                    AddLog($"{source.DisplayName}: nenhuma faixa marcada, título ignorado.");
                    continue;
                }

                var index = i;
                StatusText = $"Gravando título {index + 1} de {selected.Count}: {Path.GetFileName(job.OutputPath)}";
                AddLog($"Gravando {job.OutputPath}");
                var progress = new Progress<double>(fraction =>
                    Progress = (index + fraction) / selected.Count * 100);

                await muxer.MuxAsync(job, progress, _cancellation.Token);
                done++;
                AddLog($"Título gravado: {Path.GetFileName(job.OutputPath)}");
            }

            Progress = 100;
            AddLog($"{done} título(s) gravado(s) com sucesso em {OutputDirectory}");
            StatusText = "Concluído";
        }
        catch (OperationCanceledException)
        {
            AddLog("Gravação cancelada.");
            StatusText = "Cancelado";
        }
        catch (Exception ex) when (ex is MuxException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AddLog($"Erro: {ex.Message}");
            if (ex is MuxException { Log: { Length: > 0 } log })
            {
                AddLog(log);
            }

            StatusText = "Erro na gravação";
        }
        finally
        {
            IsBusy = false;
            IsRipping = false;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cancellation?.Cancel();

    private void AddLog(string message) => Log.Add(message);
}
