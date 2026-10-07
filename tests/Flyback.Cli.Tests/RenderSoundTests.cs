using Flyback.Cli.Commands;
using Flyback.Engine.Compile;
using Flyback.Engine.Language;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>Whether a render plays the sound, which a picture drawn from it needs whether or not the sound is written.</summary>
public class RenderSoundTests
{
    private static CompiledPatch Picture(string source)
    {
        var built = PatchLanguage.Build(source);
        built.Ok.ShouldBeTrue(built.Report);

        return built.Patch.CompileForVideo().Program;
    }

    [Fact]
    public void A_clip_whose_picture_listens_plays_the_sound_that_reaches_no_speaker()
    {
        var listening = Picture("audio.in() |> meter() |> out.color");

        RenderCommand.WantsSound(still: false, hasPicture: true, reachesSound: false, listening).ShouldBeTrue();
    }

    [Fact]
    public void A_still_whose_picture_listens_plays_the_sound_up_to_it()
    {
        var listening = Picture("audio.in() |> meter() |> out.color");

        RenderCommand.WantsSound(still: true, hasPicture: true, reachesSound: false, listening).ShouldBeTrue();
    }

    [Fact]
    public void A_clip_of_a_picture_that_does_not_listen_plays_no_sound_it_would_not_write()
    {
        var deaf = Picture("rings() |> out.color");

        RenderCommand.WantsSound(still: false, hasPicture: true, reachesSound: false, deaf).ShouldBeFalse();
    }

    [Fact]
    public void A_clip_of_a_patch_with_sound_plays_it()
    {
        var deaf = Picture("rings() |> out.color\nsine(freq: 220) |> out.left");

        RenderCommand.WantsSound(still: false, hasPicture: true, reachesSound: true, deaf).ShouldBeTrue();
    }
}
