using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TudoMKV.Core;

namespace TudoMKV.App.ViewModels;

public sealed partial class MainWindowViewModel(IMediaInspector inspector, IMuxer muxer) : ObservableObject
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

    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        IsBusy = true;
        try
        {
            foreach (var path in paths)
            {
                if (Sources.Any(s => string.Equals(s.Source.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                try
                {
                    var source = await inspector.InspectAsync(path);
                    var viewModel = new SourceViewModel(source);
                    Sources.Add(viewModel);
                    SelectedSource ??= viewModel;
                    AddLog($"Lido: {source.DisplayName} ({viewModel.Details})");
                }
                catch (Exception ex) when (ex is MuxException or FileNotFoundException or InvalidOperationException)
                {
                    AddLog($"Falha ao ler {Path.GetFileName(path)}: {ex.Message}");
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
