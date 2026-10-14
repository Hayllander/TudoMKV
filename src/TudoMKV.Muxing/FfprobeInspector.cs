using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using TudoMKV.Core;
using TudoMKV.Core.Models;

namespace TudoMKV.Muxing;

/// <summary>Descreve um arquivo usando <c>ffprobe -print_format json</c>.</summary>
public sealed class FfprobeInspector(string? ffprobePath = null) : IMediaInspector
{
    public Task<MediaSource> InspectAsync(string path, CancellationToken cancellationToken = default) =>
        InspectAsync(path, [], cancellationToken);

    /// <summary>Descreve a origem passando opções de entrada ao ffprobe (ex.: <c>-f dvdvideo -title 2</c>).</summary>
    public async Task<MediaSource> InspectAsync(string path, IReadOnlyList<string> inputArguments, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(ffprobePath ?? ToolLocator.Require("ffprobe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        string[] arguments =
        [
            "-v", "error", "-print_format", "json",
            "-show_format", "-show_streams", "-show_chapters",
            .. inputArguments, "-i", path,
        ];
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new MuxException("Não foi possível iniciar o ffprobe.");

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var log = await stderr;
            if (log.Contains("Unknown input format", StringComparison.Ordinal))
            {
                throw new MuxException(
                    "Este ffmpeg não lê discos. Use uma build com libdvdnav e libbluray, como as da BtbN ou a \"full\" da gyan.dev.",
                    log);
            }

            throw new MuxException($"O ffprobe não conseguiu ler {Path.GetFileName(Path.TrimEndingDirectorySeparator(path))}.", log);
        }

        return Parse(path, await stdout);
    }

    /// <summary>Converte a saída JSON do ffprobe no modelo do TudoMKV.</summary>
    internal static MediaSource Parse(string path, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tracks = new List<MediaTrack>();
        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                tracks.Add(ParseStream(stream));
            }
        }

        string? formatName = null;
        var duration = TimeSpan.Zero;
        long size = 0;
        if (root.TryGetProperty("format", out var format))
        {
            formatName = GetString(format, "format_name");
            _ = long.TryParse(GetString(format, "size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out size);
            if (double.TryParse(GetString(format, "duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                duration = TimeSpan.FromSeconds(seconds);
            }
        }

        var chapters = root.TryGetProperty("chapters", out var chapterArray) ? chapterArray.GetArrayLength() : 0;

        return new MediaSource
        {
            Path = path,
            FormatName = formatName,
            Duration = duration,
            ChapterCount = chapters,
            SizeBytes = size,
            Tracks = tracks,
        };
    }

    private static MediaTrack ParseStream(JsonElement stream)
    {
        var type = GetString(stream, "codec_type") switch
        {
            "video" => TrackType.Video,
            "audio" => TrackType.Audio,
            "subtitle" => TrackType.Subtitle,
            "attachment" => TrackType.Attachment,
            _ => TrackType.Data,
        };

        // Capas de MP4/MKV aparecem como vídeo com attached_pic; tratamos como anexo.
        var disposition = stream.TryGetProperty("disposition", out var d) ? d : default;
        if (type == TrackType.Video && GetFlag(disposition, "attached_pic"))
        {
            type = TrackType.Attachment;
        }

        var tags = stream.TryGetProperty("tags", out var t) ? t : default;
        var language = GetString(tags, "language");

        return new MediaTrack
        {
            Index = stream.GetProperty("index").GetInt32(),
            StreamId = ParseId(GetString(stream, "id")),
            SourceId = GetString(tags, "SOURCE_ID"),
            Type = type,
            Codec = GetString(stream, "codec_name") ?? GetString(tags, "mimetype") ?? "desconhecido",
            Language = string.IsNullOrWhiteSpace(language) ? "und" : language,
            Title = GetString(tags, "title"),
            IsDefault = GetFlag(disposition, "default"),
            IsForced = GetFlag(disposition, "forced"),
            Width = GetInt(stream, "width"),
            Height = GetInt(stream, "height"),
            Channels = GetInt(stream, "channels"),
            SampleRate = int.TryParse(GetString(stream, "sample_rate"), out var rate) ? rate : null,
            Selected = type is not TrackType.Data,
        };
    }

    private static int? ParseId(string? id) =>
        id is not null && id.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(id.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool GetFlag(JsonElement element, string name) => GetInt(element, name) == 1;
}
