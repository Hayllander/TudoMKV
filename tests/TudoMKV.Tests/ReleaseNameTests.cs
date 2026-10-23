using TudoMKV.App.Integration;
using TudoMKV.Core;
using TudoMKV.Core.Models;
using TudoMKV.Muxing;

namespace TudoMKV.Tests;

public class ReleaseNameTests
{
    private static readonly MovieInfo Movie = new() { Title = "Bastard Swordsman", Year = "1983" };

    private static MediaSource Source(params MediaTrack[] tracks) => new()
    {
        Path = Path.Combine("videos", "filme.mkv"),
        Tracks = tracks,
    };

    private static MediaTrack Video(string codec, int width, int height) =>
        new() { Index = 0, Type = TrackType.Video, Codec = codec, Width = width, Height = height };

    private static MediaTrack Audio(int index, string codec, int channels, string? profile = null) =>
        new() { Index = index, Type = TrackType.Audio, Codec = codec, Channels = channels, Profile = profile };

    [Fact]
    public void NamesADvdLikeTheExampleOfTheStandard()
    {
        var name = ReleaseName.Build(
            Movie,
            Source(Video("mpeg2video", 720, 480), Audio(1, "ac3", 2)),
            SourceKind.Dvd,
            "HAY");

        Assert.Equal("Bastard.Swordsman.1983.DVD.REMUX.DD2.0.MPEG2-HAY", name);
    }

    [Fact]
    public void DropsWhatCannotGoInAFileNameFromTheGroup()
    {
        var name = ReleaseName.Build(
            Movie,
            Source(Video("mpeg2video", 720, 480), Audio(1, "ac3", 2)),
            SourceKind.Dvd,
            " H/A:Y ");

        Assert.Equal("Bastard.Swordsman.1983.DVD.REMUX.DD2.0.MPEG2-HAY", name);
    }

    [Fact]
    public void NamesABlurayLikeTheExampleOfTheStandard()
    {
        var movie = new MovieInfo { Title = "Painted Skin", Year = "2008" };
        var audio = Audio(1, "dts", 8, "DTS-HD MA");
        audio.IsDefault = true;

        var name = ReleaseName.Build(
            movie,
            Source(Video("h264", 1920, 1080), audio),
            SourceKind.Bluray,
            "HAY");

        Assert.Equal("Painted.Skin.2008.1080p.BluRay.REMUX.AVC.DTS-HD.MA7.1-HAY", name);
    }

    [Fact]
    public void NamesACommonFileLikeABluray()
    {
        var name = ReleaseName.Build(
            new MovieInfo { Title = "Painted Skin", Year = "2008" },
            Source(Video("h264", 1920, 1080), Audio(1, "dts", 8, "DTS-HD MA")),
            SourceKind.Files,
            "HAY");

        Assert.Equal("Painted.Skin.2008.1080p.BluRay.REMUX.AVC.DTS-HD.MA7.1-HAY", name);
    }

    [Fact]
    public void SignsTheNameWithTheGroupOnlyWhenThereIsOne()
    {
        var name = ReleaseName.Build(
            Movie,
            Source(Video("mpeg2video", 720, 480), Audio(1, "ac3", 2)),
            SourceKind.Dvd,
            string.Empty);

        Assert.Equal("Bastard.Swordsman.1983.DVD.REMUX.DD2.0.MPEG2", name);
    }

    [Fact]
    public void TakesTheDefaultAudioAmongTheSelectedOnes()
    {
        var first = Audio(1, "ac3", 6);
        first.IsDefault = true;
        first.Selected = false;
        var stereo = Audio(2, "ac3", 2);
        stereo.IsDefault = true;

        var name = ReleaseName.Build(
            Movie,
            Source(Video("mpeg2video", 720, 480), first, stereo),
            SourceKind.Dvd,
            "HAY");

        Assert.Equal("Bastard.Swordsman.1983.DVD.REMUX.DD2.0.MPEG2-HAY", name);
    }

    [Fact]
    public void NamesThePcmOfABlurayByItsLosslessDestination()
    {
        MediaTrack[] tracks = [Video("h264", 1920, 1080), Audio(1, "pcm_bluray", 8)];
        DiscTracks.EncodePcm(tracks, SourceKind.Bluray);

        var name = ReleaseName.Build(
            new MovieInfo { Title = "Painted Skin", Year = "2008" },
            Source(tracks),
            SourceKind.Bluray,
            "HAY");

        Assert.Equal("Painted.Skin.2008.1080p.BluRay.REMUX.AVC.FLAC7.1-HAY", name);
    }
}
