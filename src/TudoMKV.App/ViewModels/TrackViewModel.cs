using CommunityToolkit.Mvvm.ComponentModel;
using TudoMKV.Core.Models;

namespace TudoMKV.App.ViewModels;

/// <summary>Uma linha da lista de faixas, editável pelo usuário.</summary>
public sealed partial class TrackViewModel(MediaTrack track) : ObservableObject
{
    public MediaTrack Track { get; } = track;

    public int Index => Track.Index;

    public bool CanMux => Track.CanMux;

    public string Summary => Track.Summary;

    public string TypeName => Track.Type switch
    {
        TrackType.Video => "Vídeo",
        TrackType.Audio => "Áudio",
        TrackType.Subtitle => "Legenda",
        TrackType.Attachment => "Anexo",
        _ => "Dados",
    };

    public bool Selected
    {
        get => Track.Selected;
        set => SetProperty(Track.Selected, value && CanMux, Track, (t, v) => t.Selected = v);
    }

    public string Language
    {
        get => Track.Language;
        set => SetProperty(Track.Language, value, Track, (t, v) => t.Language = v);
    }

    public string? Title
    {
        get => Track.Title;
        set => SetProperty(Track.Title, value, Track, (t, v) => t.Title = v);
    }

    public bool IsDefault
    {
        get => Track.IsDefault;
        set => SetProperty(Track.IsDefault, value, Track, (t, v) => t.IsDefault = v);
    }

    public bool IsForced
    {
        get => Track.IsForced;
        set => SetProperty(Track.IsForced, value, Track, (t, v) => t.IsForced = v);
    }
}
