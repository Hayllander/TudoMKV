using TudoMKV.Core;
using TudoMKV.Core.Models;

namespace TudoMKV.Muxing;

/// <summary>
/// Abre o que o usuário escolheu, como o MakeMKV: VIDEO_TS.IFO, index.bdmv, MovieObject.bdmv, uma pasta
/// de disco, um drive, uma ISO ou arquivos de vídeo comuns.
/// </summary>
public sealed class SourceOpener
{
    private readonly FfprobeInspector _inspector;
    private readonly IDiscReader[] _readers;

    public SourceOpener(FfprobeInspector inspector)
    {
        _inspector = inspector;
        _readers = [new BlurayReader(inspector), new DvdReader(inspector)];
    }

    public async Task<DiscContents> OpenAsync(IReadOnlyList<string> paths, TimeSpan minimumLength, CancellationToken cancellationToken = default)
    {
        if (paths.Count == 1 && _readers.FirstOrDefault(r => r.CanRead(paths[0])) is { } reader)
        {
            return await reader.ReadAsync(paths[0], minimumLength, cancellationToken);
        }

        var titles = new List<MediaSource>();
        var messages = new List<string>();
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                messages.Add($"{path} não é um arquivo de vídeo, uma pasta de DVD nem de Blu-ray.");
                continue;
            }

            try
            {
                var source = await _inspector.InspectAsync(path, cancellationToken);
                titles.Add(source);
                messages.Add($"Arquivo {Path.GetFileName(path)} foi adicionado ({source.Duration:h\\:mm\\:ss})");
            }
            catch (MuxException ex)
            {
                messages.Add($"Arquivo {Path.GetFileName(path)} foi pulado: {ex.Message}");
            }
        }

        var label = paths.Count == 1 ? Path.GetFileNameWithoutExtension(paths[0]) : "Arquivos";
        return new DiscContents(SourceKind.Files, label, titles, messages);
    }
}
