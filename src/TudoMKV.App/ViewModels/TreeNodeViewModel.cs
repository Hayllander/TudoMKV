using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TudoMKV.Core.Models;

namespace TudoMKV.App.ViewModels;

public enum NodeKind
{
    Source,
    Title,
    Track,
    Chapters,
}

/// <summary>Uma linha da árvore: o disco, um título, uma faixa ou os capítulos de um título.</summary>
public sealed partial class TreeNodeViewModel : ObservableObject
{
    private const double TypeColumnWidth = 150;
    private const double IndentPerLevel = 16;

    private readonly Action<bool>? _onChecked;

    public TreeNodeViewModel(NodeKind kind, int level, string typeText, string description, string info, bool? isChecked = null, Action<bool>? onChecked = null)
    {
        Kind = kind;
        Level = level;
        TypeText = typeText;
        Description = description;
        Info = info;
        HasCheckBox = isChecked is not null;
        _isChecked = isChecked ?? false;
        _onChecked = onChecked;
        IsExpanded = kind == NodeKind.Source;
    }

    public NodeKind Kind { get; }

    public int Level { get; }

    public string TypeText { get; }

    public string Description { get; }

    public string Info { get; }

    public bool HasCheckBox { get; }

    /// <summary>Largura da coluna "Tipo" descontando o recuo, para "Descrição" ficar alinhada em todos os níveis.</summary>
    public double TypeWidth => Math.Max(60, TypeColumnWidth - Level * IndentPerLevel);

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    /// <summary>O título a que este nó pertence (o próprio, se for um título).</summary>
    public MediaSource? Title { get; init; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    private bool _isChecked;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value))
            {
                _onChecked?.Invoke(value);
            }
        }
    }
}
