using TudoMKV.Core;
using TudoMKV.Core.Models;

namespace TudoMKV.Muxing;

/// <summary>Monta a linha de comando do ffmpeg para remuxar um <see cref="MuxJob"/> em MKV sem recodificar.</summary>
public static class FfmpegArguments
{
    /// <summary>Codecs de legenda que o Matroska não aceita por cópia direta, e o formato para o qual convertemos.</summary>
    private static readonly Dictionary<string, string> SubtitleConversions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mov_text"] = "srt",
    };

    public static IReadOnlyList<string> Build(MuxJob job)
    {
        if (job.Tracks.Count == 0)
        {
            throw new ArgumentException("Nenhuma faixa selecionada.", nameof(job));
        }

        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-y",
            "-i", job.Source.Path,
        };

        foreach (var track in job.Tracks)
        {
            args.Add("-map");
            args.Add($"0:{track.Index}");
        }

        args.AddRange(["-map_chapters", job.KeepChapters ? "0" : "-1"]);
        args.AddRange(["-c", "copy"]);

        for (var output = 0; output < job.Tracks.Count; output++)
        {
            var track = job.Tracks[output];

            if (track.Type == TrackType.Subtitle && SubtitleConversions.TryGetValue(track.Codec, out var target))
            {
                args.AddRange([$"-c:{output}", target]);
            }

            if (track.Type == TrackType.Attachment)
            {
                // Anexos não têm idioma nem disposição de reprodução.
                continue;
            }

            args.AddRange([$"-metadata:s:{output}", $"language={NormalizeLanguage(track.Language)}"]);
            args.AddRange([$"-metadata:s:{output}", $"title={track.Title ?? string.Empty}"]);
            args.AddRange([$"-disposition:{output}", Disposition(track)]);
        }

        // Evita erros de intercalação com legendas esparsas.
        args.AddRange(["-max_interleave_delta", "0"]);
        args.AddRange(["-progress", "pipe:1", "-nostats"]);
        args.AddRange(["-f", "matroska", job.OutputPath]);

        return args;
    }

    private static string Disposition(MediaTrack track) => (track.IsDefault, track.IsForced) switch
    {
        (true, true) => "default+forced",
        (true, false) => "default",
        (false, true) => "forced",
        _ => "0",
    };

    private static string NormalizeLanguage(string language) =>
        string.IsNullOrWhiteSpace(language) ? "und" : language.Trim().ToLowerInvariant();
}
