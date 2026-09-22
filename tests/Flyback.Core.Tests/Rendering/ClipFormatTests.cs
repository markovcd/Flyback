using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// The list of formats a clip can be written as, and how a file name or a saved
/// id gets to one of them (ADR-0089).
/// </summary>
/// <remarks>
/// Nothing here runs ffmpeg. What is worth pinning without it is that the table
/// is consistent with itself — every row that needs ffmpeg says so, and no two
/// rows answer to the same id — since a mistake there is one the writers would
/// only find at the end of a long render.
/// </remarks>
public class ClipFormatTests
{
    [Fact]
    public void The_two_written_here_need_nothing_installed()
    {
        ClipFormats.MotionJpegAvi.NeedsFfmpeg.ShouldBeFalse();
        ClipFormats.Wav.NeedsFfmpeg.ShouldBeFalse();
    }

    /// <summary>
    /// They are also the first row of their own list, which is what makes
    /// "row nought" the fallback everywhere a saved id is not understood.
    /// </summary>
    [Fact]
    public void The_two_written_here_lead_their_lists()
    {
        ClipFormats.Pictures[0].ShouldBe(ClipFormats.MotionJpegAvi);
        ClipFormats.Sounds[0].ShouldBe(ClipFormats.Wav);
    }

    [Fact]
    public void Every_other_format_is_ffmpegs()
    {
        foreach (var format in ClipFormats.All.Except([ClipFormats.MotionJpegAvi, ClipFormats.Wav]))
            format.NeedsFfmpeg.ShouldBeTrue(format.Id);
    }

    /// <summary>An id is what a settings file and the command line hold, so two rows cannot share one.</summary>
    [Fact]
    public void No_two_formats_answer_to_the_same_id() =>
        ClipFormats.All.Select(f => f.Id).ShouldBeUnique();

    /// <summary>
    /// A clip with sound in it is muxed in a second pass, which reads the audio
    /// arguments again — so a video format with none would write a silent file
    /// having been handed the sound.
    /// </summary>
    [Fact]
    public void Every_format_ffmpeg_writes_says_what_to_do_with_sound()
    {
        foreach (var format in ClipFormats.Pictures.Where(f => f.NeedsFfmpeg))
            format.Sound.ShouldNotBeEmpty(format.Id);
    }

    [Fact]
    public void A_sound_format_has_no_picture_arguments()
    {
        foreach (var format in ClipFormats.Sounds)
        {
            format.HasPicture.ShouldBeFalse(format.Id);
            format.Picture.ShouldBeEmpty(format.Id);
        }
    }

    [Theory]
    [InlineData("take.avi", "avi")]
    [InlineData("take.mp4", "mp4")]
    [InlineData("take.MP4", "mp4")]
    [InlineData("take.webm", "webm")]
    [InlineData("take.mov", "prores")]
    [InlineData("take.wav", "wav")]
    [InlineData("take.mp3", "mp3")]
    [InlineData("take.flac", "flac")]
    public void The_extension_names_the_format(string name, string id) =>
        ClipFormats.ByExtension(name)?.Id.ShouldBe(id);

    /// <summary>
    /// H.264 and H.265 share the container, and a bare <c>.mp4</c> has to mean
    /// the one people mean by it.
    /// </summary>
    [Fact]
    public void A_shared_extension_means_the_row_listed_first() =>
        ClipFormats.ByExtension("take.mp4").ShouldBe(ClipFormats.H264Mp4);

    [Theory]
    [InlineData("take.mkv")]
    [InlineData("take.gif")]
    [InlineData("take")]
    public void An_extension_nothing_writes_is_no_format(string name) =>
        ClipFormats.ByExtension(name).ShouldBeNull();

    [Fact]
    public void An_id_saved_into_the_wrong_list_is_not_honored()
    {
        ClipFormats.Wanted(ClipFormats.Mp3.Id, picture: true).ShouldBe(ClipFormats.MotionJpegAvi);
        ClipFormats.Wanted(ClipFormats.H264Mp4.Id, picture: false).ShouldBe(ClipFormats.Wav);
    }

    [Fact]
    public void An_id_nothing_defines_is_the_format_written_here()
    {
        ClipFormats.Wanted("av1", picture: true).ShouldBe(ClipFormats.MotionJpegAvi);
        ClipFormats.Wanted(null, picture: false).ShouldBe(ClipFormats.Wav);
    }

    /// <summary>
    /// What a machine with nothing saved starts on. The one setting whose default
    /// is a question about the machine: an AVI is always writable and is not what
    /// anybody wants when there is an alternative — ADR-0089.
    /// </summary>
    [Fact]
    public void A_machine_with_ffmpeg_starts_on_h264_and_one_without_it_on_the_avi()
    {
        ClipFormats.Preferred(ffmpeg: true).ShouldBe(ClipFormats.H264Mp4);
        ClipFormats.Preferred(ffmpeg: false).ShouldBe(ClipFormats.MotionJpegAvi);
    }

    /// <summary>
    /// The quality slider runs the opposite way to a rate factor, so the mapping
    /// has to turn over — and land on the numbers the remark in
    /// <see cref="ClipFormat.Crf"/> claims for it.
    /// </summary>
    [Theory]
    [InlineData(100, 14)]
    [InlineData(85, 18)]
    [InlineData(50, 27)]
    [InlineData(1, 40)]
    public void The_quality_maps_to_the_rate_factor_it_says_it_does(int quality, int crf) =>
        ClipFormat.Crf(quality).ShouldBe(crf);

    [Fact]
    public void A_quality_outside_the_slider_is_brought_into_it()
    {
        ClipFormat.Crf(0).ShouldBe(ClipFormat.Crf(1));
        ClipFormat.Crf(1000).ShouldBe(ClipFormat.Crf(100));
    }

    [Fact]
    public void The_quality_reaches_the_arguments() =>
        ClipFormats.H264Mp4.PictureArguments(85).ShouldContain("-crf 18");

    /// <summary>
    /// A placeholder left unfilled would reach ffmpeg as the literal text, which
    /// it would refuse — after a whole clip had been rendered into the pipe.
    /// </summary>
    [Fact]
    public void Nothing_is_left_standing_in_for_the_quality()
    {
        foreach (var format in ClipFormats.Pictures)
            format.PictureArguments(60).Contains('{').ShouldBeFalse(format.Id);
    }
}
