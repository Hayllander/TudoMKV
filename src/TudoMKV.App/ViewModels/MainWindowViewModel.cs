using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TudoMKV.Core;
using TudoMKV.Core.Models;

namespace TudoMKV.App.ViewModels;

public sealed partial class MainWindowViewModel(IMediaInspector inspector, IDiscReader discReader, IMuxer muxer) : ObservableObject
{
    private bool _queueRunning;

    public ObservableCollection<SourceViewModel> Sources { get; } = [];

    public ObservableCollection<JobViewModel> Jobs { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnqueueSelectedCommand))]
    public partial SourceViewModel? SelectedSource { get; set; }

    /// <summary>Pasta de saída; vazio grava ao lado do arquivo de origem.</summary>
    [ObservableProperty]
    public partial string? OutputDirectory { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Títulos de disco mais curtos que isto ficam ocultos (menus, avisos, trailers).</summary>
    public TimeSpan MinimumTitleLength { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Adiciona arquivos de vídeo, pastas de DVD, imagens ISO ou drives.</summary>
    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        IsBusy = true;
        try
        {
            foreach (var path in paths)
            {
                try
                {
                    if (discReader.CanRead(path))
                    {
                        await AddDiscAsync(path);
                    }
                    else if (File.Exists(path))
                    {
                        await AddFileAsync(path);
                    }
                    else
                    {
                        AddLog($"{path}: não é um arquivo de vídeo nem um DVD.");
                    }
                }
                catch (Exception ex) when (ex is MuxException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
                {
                    AddLog($"Falha ao ler {path}: {ex.Message}");
                    if (ex is MuxException { Log: { Length: > 0 } log })
                    {
                        AddLog(log);
                    }
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddFileAsync(string path)
    {
        var source = await inspector.InspectAsync(path);
        if (AddSource(source) is { } viewModel)
        {
            AddLog($"Lido: {source.DisplayName} ({viewModel.Details})");
        }
    }

    private async Task AddDiscAsync(string path)
    {
        AddLog($"Lendo disco em {path}…");
        var contents = await discReader.ReadAsync(path, MinimumTitleLength);
        foreach (var title in contents.Titles)
        {
            AddSource(title);
        }

        var hidden = contents.HiddenShortTitles > 0
            ? $", {contents.HiddenShortTitles} curtos ocultos (menos de {MinimumTitleLength.TotalMinutes:0} min)"
            : string.Empty;
        AddLog($"DVD {contents.Label}: {contents.Titles.Count} títulos{hidden}.");
    }

    private SourceViewModel? AddSource(MediaSource source)
    {
        if (Sources.Any(s => s.Source.Key == source.Key))
        {
            return null;
        }

        var viewModel = new SourceViewModel(source);
        Sources.Add(viewModel);
        SelectedSource ??= viewModel;
        return viewModel;
    }

    private bool HasSelection => SelectedSource is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemoveSource()
    {
        if (SelectedSource is { } source)
        {
            Sources.Remove(source);
            SelectedSource = Sources.FirstOrDefault();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void EnqueueSelected()
    {
        if (SelectedSource is { } source)
        {
            Enqueue(source);
        }
    }

    [RelayCommand]
    private void EnqueueAll()
    {
        foreach (var source in Sources)
        {
            Enqueue(source);
        }
    }

    [RelayCommand]
    private void ClearFinished()
    {
        foreach (var job in Jobs.Where(j => j.State is JobState.Done or JobState.Failed or JobState.Canceled).ToList())
        {
            Jobs.Remove(job);
        }
    }

    private void Enqueue(SourceViewModel source)
    {
        var directory = string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory;
        var job = MuxJob.FromSelection(source.Source, directory);
        if (job.Tracks.Count == 0)
        {
            AddLog($"{source.DisplayName}: nenhuma faixa marcada.");
            return;
        }

        Jobs.Add(new JobViewModel(job));
        _ = RunQueueAsync();
    }

    /// <summary>Processa a fila um trabalho por vez.</summary>
    private async Task RunQueueAsync()
    {
        if (_queueRunning)
        {
            return;
        }

        _queueRunning = true;
        try
        {
            while (Jobs.FirstOrDefault(j => j.State == JobState.Waiting) is { } job)
            {
                await RunJobAsync(job);
            }
        }
        finally
        {
            _queueRunning = false;
        }
    }

    private async Task RunJobAsync(JobViewModel job)
    {
        job.State = JobState.Running;
        AddLog($"Gravando {job.OutputPath}");
        var progress = new Progress<double>(fraction => job.Percent = fraction * 100);

        try
        {
            await muxer.MuxAsync(job.Job, progress, job.CancellationToken);
            job.Percent = 100;
            job.State = JobState.Done;
            AddLog($"Concluído: {job.Name}");
        }
        catch (OperationCanceledException)
        {
            job.State = JobState.Canceled;
            AddLog($"Cancelado: {job.Name}");
        }
        catch (Exception ex) when (ex is MuxException or FileNotFoundException or IOException or InvalidOperationException)
        {
            job.Error = ex.Message;
            job.State = JobState.Failed;
            AddLog($"Erro em {job.Name}: {ex.Message}");
            if (ex is MuxException { Log: { Length: > 0 } log })
            {
                AddLog(log);
            }
        }
    }

    private void AddLog(string message) => Log.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
}
