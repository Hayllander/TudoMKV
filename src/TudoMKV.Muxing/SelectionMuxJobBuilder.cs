using TudoMKV.Core;
using TudoMKV.Core.Models;

namespace TudoMKV.Muxing;

/// <summary>Prepara o job final de mux; em DVDs problemáticos pode usar VOBs físicos para vídeo e áudio.</summary>
public sealed class SelectionMuxJobBuilder(FfprobeInspector inspector)
{
    private const long SectorSize = 2048;

    public async Task<MuxJob> CreateAsync(
        MediaSource source,
        string? outputDirectory = null,
        bool keepChapters = true,
        string? outputBaseName = null,
        CancellationToken cancellationToken = default)
    {
        var job = MuxJob.FromSelection(source, outputDirectory, keepChapters, outputBaseName);
        return await TryBuildDvdHybridAsync(job, cancellationToken);
    }

    private async Task<MuxJob> TryBuildDvdHybridAsync(MuxJob job, CancellationToken cancellationToken)
    {
        var source = job.Source;
        if (!LooksLikeDvdTitle(source) || source.DiscTitleSet is not { } titleSet)
        {
            return job;
        }

        var vobs = FindCompleteTitleSetVobs(source.Path, titleSet, source.SizeBytes);
        if (vobs.Count == 0)
        {
            return job;
        }

        var mergedVob = Path.Combine(
            Path.GetTempPath(),
            "TudoMKV",
            $"dvd_vts_{titleSet:00}_{Guid.NewGuid():N}.vob");

        Directory.CreateDirectory(Path.GetDirectoryName(mergedVob)!);
        await MergeFilesAsync(vobs, mergedVob, cancellationToken);

        try
        {
            var merged = await inspector.InspectAsync(mergedVob, cancellationToken);
            DiscTracks.Prepare(merged.Tracks, SourceKind.Dvd);

            if (!TryBuildHybridTracks(source, merged, out var tracks))
            {
                File.Delete(mergedVob);
                return job;
            }

            return new MuxJob
            {
                Source = CloneSource(merged, source, mergedVob),
                ExtraSources = [source],
                OutputPath = job.OutputPath,
                Tracks = tracks,
                KeepChapters = job.KeepChapters,
                ChaptersSourceIndex = 1,
                TemporaryFiles = [mergedVob],
            };
        }
        catch
        {
            TryDelete(mergedVob);
            throw;
        }
    }

    private static bool LooksLikeDvdTitle(MediaSource source) =>
        source.IsDisc
        && source.TitleNumber is not null
        && source.InputArguments.Count >= 2
        && string.Equals(source.InputArguments[0], "-f", StringComparison.Ordinal)
        && string.Equals(source.InputArguments[1], "dvdvideo", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> FindCompleteTitleSetVobs(string inputPath, int titleSet, long expectedBytes)
    {
        var baseDirectory = Directory.Exists(Path.Combine(inputPath, "VIDEO_TS"))
            ? Path.Combine(inputPath, "VIDEO_TS")
            : inputPath;
        if (!Directory.Exists(baseDirectory))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(baseDirectory, $"VTS_{titleSet:00}_*.VOB")
            .Where(path => !path.EndsWith($"_{0}.VOB", StringComparison.OrdinalIgnoreCase))
            .OrderBy(FilePartNumber)
            .ToList();
        if (files.Count == 0)
        {
            return [];
        }

        var totalBytes = files.Sum(path => new FileInfo(path).Length);
        return Math.Abs(totalBytes - expectedBytes) <= SectorSize ? files : [];
    }

    private static int FilePartNumber(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var underscore = fileName.LastIndexOf('_');
        return underscore >= 0 && int.TryParse(fileName[(underscore + 1)..], out var number) ? number : int.MaxValue;
    }

    private static async Task MergeFilesAsync(IReadOnlyList<string> files, string outputPath, CancellationToken cancellationToken)
    {
        await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        foreach (var file in files)
        {
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private static bool TryBuildHybridTracks(MediaSource original, MediaSource merged, out IReadOnlyList<MediaTrack> tracks)
    {
        // As faixas geradas não existem no disco: quem manda no pareamento é o vídeo e o áudio de verdade.
        var originalAv = original.Tracks
            .Where(track => !track.IsGenerated && track.Type is TrackType.Video or TrackType.Audio)
            .ToList();
        var mergedAv = merged.Tracks
            .Where(track => !track.IsGenerated && track.Type is TrackType.Video or TrackType.Audio)
            .ToList();
        if (!originalAv.Select(track => track.Type).SequenceEqual(mergedAv.Select(track => track.Type)))
        {
            tracks = [];
            return false;
        }

        var originalIndexes = originalAv
            .Select((track, index) => (track, index))
            .ToDictionary(item => item.track, item => item.index);

        tracks = original.Tracks
            .Where(track => track.Selected && track.CanMux)
            .Select(track =>
            {
                if (track.IsGenerated)
                {
                    // O estéreo também vem do VOB físico, do mesmo áudio de onde saiu o mono.
                    var mono = originalIndexes[track.ConvertedFrom!];
                    return CloneTrack(track, inputIndex: 0, index: mergedAv[mono].Index);
                }

                if (track.Type is not (TrackType.Video or TrackType.Audio))
                {
                    return CloneTrack(track, inputIndex: 1);
                }

                var replacement = mergedAv[originalIndexes[track]];
                return CloneTrack(
                    track,
                    inputIndex: 0,
                    index: replacement.Index,
                    streamId: replacement.StreamId,
                    codec: replacement.Codec,
                    profile: replacement.Profile,
                    width: replacement.Width,
                    height: replacement.Height,
                    channels: replacement.Channels,
                    sampleRate: replacement.SampleRate);
            })
            .ToList();

        return true;
    }

    /// <summary>
    /// No VOB cru muitos quadros de vídeo vêm só com DTS (o DVD só exige PTS a cada 0,7 s) e o Matroska
    /// recusa pacote sem PTS ("Can't write packet with unknown timestamp"). O <c>+genpts</c> faz o ffmpeg
    /// deduzir o PTS pela ordem dos quadros, como o demuxer dvdvideo e o MakeMKV fazem.
    /// </summary>
    internal static readonly string[] PhysicalVobInputArguments = ["-fflags", "+genpts"];

    private static MediaSource CloneSource(MediaSource merged, MediaSource original, string path) => new()
    {
        Path = path,
        InputArguments = [.. PhysicalVobInputArguments, .. merged.InputArguments],
        Name = original.Name,
        TitleNumber = original.TitleNumber,
        DiscTitleSet = original.DiscTitleSet,
        OutputBaseName = original.OutputBaseName,
        IsDisc = original.IsDisc,
        ChaptersFile = merged.ChaptersFile,
        SizeBytes = original.SizeBytes,
        FormatName = merged.FormatName,
        Duration = original.Duration > TimeSpan.Zero ? original.Duration : merged.Duration,
        ChapterCount = original.ChapterCount,
        Tracks = merged.Tracks,
    };

    private static MediaTrack CloneTrack(
        MediaTrack track,
        int inputIndex,
        int? index = null,
        int? streamId = null,
        string? codec = null,
        string? profile = null,
        int? width = null,
        int? height = null,
        int? channels = null,
        int? sampleRate = null) => new()
        {
            InputIndex = inputIndex,
            Index = index ?? track.Index,
            StreamId = streamId ?? track.StreamId,
            SourceId = track.SourceId,
            Type = track.Type,
            Codec = codec ?? track.Codec,
            Profile = profile ?? track.Profile,
            Language = track.Language,
            Title = track.Title,
            IsDefault = track.IsDefault,
            IsForced = track.IsForced,
            Selected = track.Selected,
            Width = width ?? track.Width,
            Height = height ?? track.Height,
            Channels = channels ?? track.Channels,
            SampleRate = sampleRate ?? track.SampleRate,
            Conversion = track.Conversion,
            ConvertedFrom = track.ConvertedFrom,
            EncodeTo = track.EncodeTo,
        };

    private static void TryDelete(string path)
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
