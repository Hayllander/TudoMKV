using System.Collections.ObjectModel;
using TudoMKV.Core.Models;

namespace TudoMKV.App.ViewModels;

/// <summary>Um arquivo adicionado à lista, com suas faixas.</summary>
public sealed class SourceViewModel(MediaSource source)
{
    public MediaSource Source { get; } = source;

    public ObservableCollection<TrackViewModel> Tracks { get; } =
        new(source.Tracks.Select(t => new TrackViewModel(t)));

    public string DisplayName => Source.DisplayName;

    public string Details
    {
        get
        {
            var parts = new List<string> { Source.Duration.ToString(@"h\:mm\:ss") };
            parts.Add($"{Source.Tracks.Count} faixas");
            if (Source.ChapterCount > 0)
            {
                parts.Add($"{Source.ChapterCount} capítulos");
            }

            return string.Join(" · ", parts);
        }
    }
}
