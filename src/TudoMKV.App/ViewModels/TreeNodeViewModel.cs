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

    /// <summary>Caminho do ícone conforme o tipo do nó.</summary>
    public string IconData
    {
        get
        {
            if (Kind == NodeKind.Source)
            {
                return "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,16A4,4 0 0,1 8,12A4,4 0 0,1 12,8A4,4 0 0,1 16,12A4,4 0 0,1 12,16M12,10.5A1.5,1.5 0 0,0 10.5,12A1.5,1.5 0 0,0 12,13.5A1.5,1.5 0 0,0 13.5,12A1.5,1.5 0 0,0 12,10.5Z";
            }

            if (Kind == NodeKind.Title)
            {
                return "M18,4L20,8H17L15,4H13L15,8H12L10,4H8L10,8H7L5,4H4A2,2 0 0,0 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V4H18Z";
            }

            if (Kind == NodeKind.Chapters)
            {
                return "M19,21L15.5,17L16.9,15.6L19,17.7L22.6,14.1L24,15.5L19,21M15,5H5V7H15V5M15,9H5V11H15V9M15,13H5V15H15V13M5,17H11V19H5V17Z";
            }

            // Faixas: vídeo, áudio, legenda, anexo
            if (TypeText.Contains("ídeo"))
            {
                return "M17,10.5V7A1,1 0 0,0 16,6H4A1,1 0 0,0 3,7V17A1,1 0 0,0 4,18H16A1,1 0 0,0 17,17V13.5L21,17.5V6.5L17,10.5Z";
            }

            if (TypeText.Contains("udio"))
            {
                return "M12,3V12.26A4,4 0 1,0 14,16V8H18V5H14V3H12M10,20A2,2 0 0,1 8,18A2,2 0 0,1 10,16A2,2 0 0,1 12,18A2,2 0 0,1 10,20Z";
            }

            if (TypeText.Contains("egenda"))
            {
                return "M20,4H4A2,2 0 0,0 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V6A2,2 0 0,0 20,4M4,12H20V18H4V12M4,6H20V10H4V6Z";
            }

            if (TypeText.Contains("nexo"))
            {
                return "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z";
            }

            return "";
        }
    }

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
