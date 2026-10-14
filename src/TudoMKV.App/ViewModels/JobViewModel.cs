using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TudoMKV.Core;

namespace TudoMKV.App.ViewModels;

public enum JobState
{
    Waiting,
    Running,
    Done,
    Failed,
    Canceled,
}

/// <summary>Um item da fila de gravação.</summary>
public sealed partial class JobViewModel(MuxJob job) : ObservableObject
{
    private readonly CancellationTokenSource _cancellation = new();

    public MuxJob Job { get; } = job;

    public string Name => Path.GetFileName(Job.OutputPath);

    public string OutputPath => Job.OutputPath;

    public CancellationToken CancellationToken => _cancellation.Token;

    [ObservableProperty]
    public partial double Percent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial JobState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? Error { get; set; }

    public string StatusText => State switch
    {
        JobState.Waiting => "Na fila",
        JobState.Running => "Gravando",
        JobState.Done => "Concluído",
        JobState.Canceled => "Cancelado",
        _ => $"Erro: {Error}",
    };

    private bool CanCancel => State is JobState.Waiting or JobState.Running;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancellation.Cancel();
        if (State == JobState.Waiting)
        {
            State = JobState.Canceled;
        }
    }
}
