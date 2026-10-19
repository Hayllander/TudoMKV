using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TudoMKV.App.Configuration;
using TudoMKV.App.Integration;
using TudoMKV.Core;
using TudoMKV.Core.Models;
using TudoMKV.Disc.Dvd;
using TudoMKV.Muxing;

namespace TudoMKV.App.ViewModels;

/// <summary>
/// Tela única no estilo MakeMKV: abre uma origem, mostra a árvore Disco &gt; Título &gt; Faixas,
/// e grava os títulos marcados na pasta de saída.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly SourceOpener _opener;
    private readonly IMuxer _muxer;
    private readonly SelectionMuxJobBuilder _jobBuilder;
    private readonly MetadataLookup _metadataLookup;
    private readonly MkvTagWriter _mkvTagWriter;
    private readonly List<(MediaSource Source, TreeNodeViewModel Title, TreeNodeViewModel? Chapters)> _titles = [];
    private CancellationTokenSource? _cancellation;
    private SourceKind _kind = SourceKind.Files;

    public MainWindowViewModel(SourceOpener opener, IMuxer muxer, SelectionMuxJobBuilder jobBuilder, MetadataLookup metadataLookup, MkvTagWriter mkvTagWriter)
    {
        _opener = opener;
        _muxer = muxer;
        _jobBuilder = jobBuilder;
        _metadataLookup = metadataLookup;
        _mkvTagWriter = mkvTagWriter;
        Log.CollectionChanged += (_, _) => OnPropertyChanged(nameof(LogText));
    }

    /// <summary>Títulos mais curtos que isto são pulados (menus, avisos, trailers), como no MakeMKV.</summary>
    public TimeSpan MinimumTitleLength { get; set; } = TimeSpan.FromSeconds(120);

    public ObservableCollection<TreeNodeViewModel> Nodes { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    public string LogText => string.Join(Environment.NewLine, Log);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InfoText), nameof(InfoHeader), nameof(InfoRows), nameof(InfoHint))]
    public partial TreeNodeViewModel? SelectedNode { get; set; }

    public string InfoText => SelectedNode?.Info
        ?? "Abra um disco ou arquivo pelo menu Arquivo, pelos botões acima ou arrastando para esta janela.\n\n" +
           "DVD: escolha o VIDEO_TS.IFO. A pasta VIDEO_TS também funciona, mas o fluxo principal é pelo IFO.\n" +
           "Blu-ray: escolha o index.bdmv ou o MovieObject.bdmv dentro da pasta BDMV.";

    /// <summary>Primeira linha das informações ("Informações do Título"), mostrada como cabeçalho.</summary>
    public string InfoHeader => SelectedNode is null ? "Como começar" : InfoText.Split('\n')[0];

    /// <summary>As linhas "Nome: valor" das informações, para mostrar em duas colunas.</summary>
    public IReadOnlyList<InfoRow> InfoRows => SelectedNode is null
        ? []
        : InfoText.Split('\n').Skip(1).Select(InfoRow.Parse).ToList();

    /// <summary>Texto livre quando não há nada selecionado.</summary>
    public string? InfoHint => SelectedNode is null ? InfoText : null;

    /// <summary>"2 títulos · 4.1 GB", abaixo do botão Make MKV.</summary>
    public string SelectionSummary
    {
        get
        {
            var chosen = _titles.Where(t => t.Title.IsChecked).Select(t => t.Source).ToList();
            if (chosen.Count == 0)
            {
                return _titles.Count == 0 ? "Nada aberto" : "Nenhum título marcado";
            }

            var size = Describe.Size(chosen.Sum(s => s.SizeBytes));
            var titles = Describe.Plural(chosen.Count, "título", "títulos");
            return size.Length > 0 ? $"{titles}  ·  {size}" : titles;
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MakeMkvCommand))]
    public partial string? OutputDirectory { get; set; }

    /// <summary>Pasta gravada na última operação: é ela que o link abaixo do log abre no Explorer.</summary>
    [ObservableProperty]
    public partial string? OutputFolderLink { get; set; }

    /// <summary>
    /// Abre o log com a versão em uso e avisa, sem travar a tela, quando houver versão mais nova publicada.
    /// </summary>
    public async Task StartupAsync()
    {
        AddLog(UpdateChecker.StartupLine());
        if (await UpdateChecker.NewerVersionLineAsync() is { } newer)
        {
            AddLog(newer);
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MakeMkvCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(VerifyMetadataCommand))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    public partial bool IsRipping { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Pronto";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyMetadataCommand))]
    public partial string? MetadataReference { get; set; }

    public MovieInfo? LastVerifiedMovie { get; private set; }

    public async Task OpenAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Lendo…";

        // A origem anterior sai da tela; libera as ISOs que montamos para ela.
        IsoMount.ReleaseAll();

        // Outro disco começa do zero: a console fica só com o que este disco disser.
        ClearLog();

        try
        {
            var contents = await _opener.OpenAsync(paths, MinimumTitleLength);
            foreach (var message in contents.Messages)
            {
                AddLog(message);
            }

            // Nada aproveitável entre arquivos soltos: não mostra uma origem vazia nem troca a pasta de saída.
            if (contents.Kind != SourceKind.Files || contents.Titles.Count > 0)
            {
                Show(contents);
                OutputDirectory = DefaultOutputDirectory(contents, paths);
                ClearMovieTags();
            }
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
            SelectionChanged();
        }
    }

    private void SelectionChanged()
    {
        MakeMkvCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectionSummary));
    }

    private void Show(DiscContents contents)
    {
        Nodes.Clear();
        _titles.Clear();
        _kind = contents.Kind;

        var root = new TreeNodeViewModel(
            NodeKind.Source, 0, Describe.KindName(contents.Kind),
            $"{contents.Label}  ·  {Describe.Plural(contents.Titles.Count, "título", "títulos")}",
            $"Informações da Origem\nTipo: {Describe.KindName(contents.Kind)}\nNome: {contents.Label}\nTítulos: {contents.Titles.Count}");

        foreach (var source in contents.Titles)
        {
            var title = new TreeNodeViewModel(
                NodeKind.Title, 1, Describe.TitleType(source), Describe.Title(source), TitleInfo(source),
                isChecked: true, onChecked: _ => SelectionChanged())
            {
                Title = source,
            };

            foreach (var track in source.Tracks.Where(t => t.CanMux && !t.IsGenerated))
            {
                var node = new TreeNodeViewModel(
                    NodeKind.Track, 2, Describe.TrackTypeName(track.Type), Describe.Track(track), Describe.TrackInfo(track),
                    isChecked: track.Selected, onChecked: value => track.Selected = value)
                {
                    Title = source,
                };
                title.Children.Add(node);

                // As faixas geradas ficam sob a faixa de onde saem (o estéreo logo abaixo do mono).
                foreach (var generated in source.Tracks.Where(t => t.ConvertedFrom == track))
                {
                    node.Children.Add(new TreeNodeViewModel(
                        NodeKind.Track, 3, Describe.TrackTypeName(generated.Type), Describe.Track(generated), Describe.TrackInfo(generated),
                        isChecked: generated.Selected, onChecked: value => generated.Selected = value)
                    {
                        Title = source,
                    });
                    node.IsExpanded = true;
                }
            }

            TreeNodeViewModel? chapters = null;
            if (source.ChapterCount > 0)
            {
                chapters = new TreeNodeViewModel(
                    NodeKind.Chapters, 2, "Capítulos", Describe.Plural(source.ChapterCount, "capítulo", "capítulos"),
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

        var source = Path.GetFullPath(paths[0]);
        string? originDir = File.Exists(source)
            ? Path.GetDirectoryName(source)
            : (Directory.Exists(source) ? source : null);

        if (originDir is not null)
        {
            try
            {
                var root = Path.GetPathRoot(originDir);
                if (root is not null && Directory.Exists(root) && new DriveInfo(root).DriveType == DriveType.CDRom)
                {
                    originDir = null;
                }
            }
            catch
            {
                // ignora; usa a pasta de origem
            }
        }

        var label = string.Join("_", contents.Label
            .Split(Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToArray(), StringSplitOptions.TrimEntries));

        if (originDir is null)
        {
            var videos = Path.GetDirectoryName(MuxJob.DefaultDiscOutputDirectory)!;
            return Path.Combine(videos, label);
        }

        return Path.Combine(originDir, label);
    }

    private bool CanMakeMkv() =>
        !IsBusy && !string.IsNullOrWhiteSpace(OutputDirectory) && _titles.Any(t => t.Title.IsChecked);

    private bool CanVerifyMetadata() =>
        !IsBusy && !string.IsNullOrWhiteSpace(MetadataReference);

    [RelayCommand(CanExecute = nameof(CanVerifyMetadata))]
    private async Task VerifyMetadataAsync()
    {
        IsBusy = true;
        StatusText = "Consultando metadados…";
        LastVerifiedMovie = null;
        try
        {
            var movie = await _metadataLookup.FetchAsync(MetadataReference!);
            LastVerifiedMovie = movie;
            AddLog($"{SourceOf(movie)}: metadados do filme guardados para o MKV");
            AddLog($"Título: {movie.Title} ({movie.Year})");
            StatusText = "Tags atualizadas";
        }
        catch (Exception ex)
        {
            AddLog($"Falha ao consultar os metadados: {ex.Message}");
            StatusText = "Erro na consulta";
        }
        finally
        {
            IsBusy = false;
            if (StatusText != "Erro na consulta")
            {
                StatusText = "Pronto";
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanMakeMkv))]
    private async Task MakeMkvAsync()
    {
        var selected = _titles.Where(t => t.Title.IsChecked).ToList();
        IsBusy = true;
        IsRipping = true;
        OutputFolderLink = null;
        _cancellation = new CancellationTokenSource();
        var done = 0;
        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                var (source, _, chapters) = selected[i];
                var job = await _jobBuilder.CreateAsync(
                    source,
                    OutputDirectory,
                    keepChapters: chapters?.IsChecked ?? true,
                    outputBaseName: ReleaseBaseName(source, selected.Count),
                    cancellationToken: _cancellation.Token);
                if (job.Tracks.Count == 0)
                {
                    AddLog($"{source.DisplayName}: nenhuma faixa marcada, título ignorado.");
                    continue;
                }

                var index = i;
                StatusText = $"Gravando título {index + 1} de {selected.Count}: {Path.GetFileName(job.OutputPath)}";
                AddLog($"Gravando {job.OutputPath}");
                if (job.ExtraSources.Count > 0 && job.ChaptersSourceIndex > 0)
                {
                    AddLog($"{source.DisplayName}: usando VOBs físicos para vídeo/áudio e dvdvideo para legendas/capítulos.");
                }
                var progress = new Progress<double>(fraction =>
                    Progress = (index + fraction) / selected.Count * 100);

                try
                {
                    await _muxer.MuxAsync(job, progress, _cancellation.Token);
                    await TryApplyTagsAsync(job.OutputPath, _cancellation.Token);
                    done++;
                    AddLog($"Título gravado: {Path.GetFileName(job.OutputPath)}");
                }
                finally
                {
                    CleanupTemporaryFiles(job.TemporaryFiles);
                }
            }

            Progress = 100;
            AddLog($"{done} título(s) gravado(s) com sucesso em \"{OutputDirectory}\"");
            StatusText = "Concluído";

            // A pasta não abre sozinha: o link abaixo do log é que leva até ela.
            if (done > 0)
            {
                OutputFolderLink = OutputDirectory;
            }
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

    /// <summary>
    /// Esvazia a console: o texto do disco anterior e a pasta do último MKV deixam de valer juntos.
    /// </summary>
    [RelayCommand]
    private void ClearLog()
    {
        Log.Clear();
        OutputFolderLink = null;
    }

    /// <summary>
    /// As tags pertencem ao disco que estava aberto: ao abrir outro, o filme verificado é esquecido. Sem isso,
    /// um disco sem IMDb informado sairia com os metadados do filme anterior.
    /// </summary>
    private void ClearMovieTags()
    {
        LastVerifiedMovie = null;
        MetadataReference = null;
    }

    /// <summary>Como a ficha foi obtida, para o log dizer de onde vieram os metadados.</summary>
    private static string SourceOf(MovieInfo movie) =>
        movie.TmdbId.Length > 0 ? $"TMDb {movie.TmdbId}" : $"IMDb {movie.ImdbId}";

    /// <summary>
    /// Nome do MKV quando o filme foi identificado no TMDb ou no IMDb: sai no formato das releases de cena.
    /// Com mais de um título marcado cada um fica com o nome do disco (_tNN), que é o que os distingue;
    /// sem ficha verificada, também vale o nome do disco.
    /// </summary>
    private string? ReleaseBaseName(MediaSource source, int selectedCount)
    {
        if (selectedCount != 1 || LastVerifiedMovie is not { } movie)
        {
            return null;
        }

        var group = AppSettingsStore.Load().Muxing.ReleaseGroup.Trim();
        return ReleaseName.Build(movie, source, _kind, group);
    }

    /// <summary>
    /// Monta as etiquetas na hora, a partir do filme que a API devolveu. Sem consulta verificada não há
    /// etiqueta nenhuma; com uma guardada, o XML vai direto para o mkvpropedit, sem arquivo na pasta do app.
    /// </summary>
    private async Task TryApplyTagsAsync(string outputPath, CancellationToken cancellationToken)
    {
        if (LastVerifiedMovie is not { } movie)
        {
            return;
        }

        try
        {
            await _mkvTagWriter.ApplyGlobalTagsAsync(outputPath, TagXml.Build(movie), cancellationToken);
            AddLog($"Tags globais aplicadas: {movie.Title} ({movie.Year})");
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or MuxException)
        {
            AddLog($"Aviso: não foi possível aplicar as tags globais no MKV: {ex.Message}");
            if (ex is MuxException { Log: { Length: > 0 } log })
            {
                AddLog(log);
            }
        }
    }

    /// <summary>Abre no Explorer a pasta gravada, a partir do link mostrado abaixo do log.</summary>
    [RelayCommand]
    private void OpenOutputFolder() => RevealInExplorer(OutputFolderLink);

    /// <summary>Abre a pasta dos MKVs no Explorer. Falhar nisso não pode invalidar a gravação.</summary>
    private void RevealInExplorer(string? folder)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            process?.Dispose();
        }
        catch (Exception ex)
        {
            AddLog($"Aviso: não foi possível abrir a pasta no Explorer: {ex.Message}");
        }
    }

    private void AddLog(string message) => Log.Add(message);

    private void CleanupTemporaryFiles(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

/// <summary>Uma linha "Nome: valor" do painel de informações.</summary>
public sealed record InfoRow(string Name, string Value)
{
    public static InfoRow Parse(string line)
    {
        var colon = line.IndexOf(": ", StringComparison.Ordinal);
        return colon < 0 ? new InfoRow(line, "") : new InfoRow(line[..colon], line[(colon + 2)..]);
    }
}
