using System.Text.Json.Nodes;
using Flyback.Core;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Speech;

/// <summary>The Speak, played from the start of the patch with its words set on the node.</summary>
public class SpeakTests
{
    private const string SpeakType = "flyback.speech.speak";

    private const int Rate = GlobalConstants.SampleRate;

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    [Fact]
    public void The_speech_plugin_offers_it_with_its_words_on_the_node()
    {
        var def = Catalog.Get(SpeakType).ShouldNotBeNull();

        def.Name.ShouldBe("Speak");
        def.Outputs.Select(o => o.Name).ShouldBe(["out", "open", "round", "line", "reveal"]);
        def.Extras.ShouldHaveSingleItem().Fields.Select(f => f.Key).ShouldBe(["words", "mode", "notes"]);
        Catalog.ProviderOf(SpeakType)!.Id.ShouldBe("flyback.speech");
    }

    [Fact]
    public void A_word_spelled_in_sounds_is_said_as_the_word_is()
    {
        Said("[hh ax l ow]").ShouldBe(Said("hello"));
    }

    [Fact]
    public void A_word_it_knows_no_rule_for_is_still_said_by_its_letters()
    {
        var said = Said("Glimbo");

        Rms(said, 0f, 0.5f).ShouldBeGreaterThan(0.02);
    }

    [Fact]
    public void Faster_it_is_done_sooner()
    {
        var words = "Every sound here is made by filters.";

        End(Said(words, ("speed", 2f))).ShouldBeLessThan(End(Said(words)) * 0.6f);
    }

    [Fact]
    public void It_says_the_words_again_every_so_many_seconds()
    {
        var once = Said("Hi.", ("every", 0f));
        var repeated = Said("Hi.", ("every", 1f));

        Rms(once, 1f, 1.4f).ShouldBeLessThan(1e-4);
        Rms(repeated, 1f, 1.4f).ShouldBeGreaterThan(0.02);
    }

    [Fact]
    public void Sung_each_syllable_is_on_its_note_in_time()
    {
        // Two beats of A3 then two of E4, at 120 a minute: a second each.
        var sung = Sung("la la", "A3@2 E4@2", 120f);

        Pitch(sung, 0.55f).ShouldBe(220d, 220d * 0.03);
        Pitch(sung, 1.55f).ShouldBe(329.6d, 329.6d * 0.03);
    }

    [Fact]
    public void Sung_the_tune_comes_round_again()
    {
        // A beat each at 120 a minute, so the tune is a second long.
        var sung = Sung("la la", "A3 E4", 120f);

        Pitch(sung, 0.75f).ShouldBe(329.6d, 329.6d * 0.03);
        Pitch(sung, 1.25f).ShouldBe(220d, 220d * 0.03);
    }

    [Fact]
    public void Sung_a_rest_is_silent_and_takes_no_syllable()
    {
        // Half a second each at 120 a minute: A3, a rest, then E4 on the second syllable.
        var sung = Sung("la la", "A3 ~ E4", 120f);

        Pitch(sung, 0.2f).ShouldBe(220d, 220d * 0.03);
        Rms(sung, 0.6f, 0.95f).ShouldBeLessThan(1e-4);
        Pitch(sung, 1.2f).ShouldBe(329.6d, 329.6d * 0.03);
    }

    [Fact]
    public void Sung_rests_after_the_last_note_lengthen_the_tune()
    {
        // A beat of A3 and three of rest at 120 a minute: two seconds round.
        var sung = Sung("la", "A3 ~@3", 120f, seconds: 3);

        Rms(sung, 0.7f, 1.95f).ShouldBeLessThan(1e-4);
        Rms(sung, 2.05f, 2.4f).ShouldBeGreaterThan(0.02);
    }

    [Fact]
    public void Sung_its_mouth_shuts_on_a_rest()
    {
        var open = Played(new JsonObject { ["words"] = "[aa aa]", ["mode"] = "sing", ["notes"] = "A3 ~" }, 1, 2, ("bpm", 120f));

        open[(int)(0.3 * Rate)].ShouldBeGreaterThan(0.5);
        open[(int)(0.8 * Rate)].ShouldBe(0d, 1e-6);
    }

    [Fact]
    public void Its_mouth_opens_on_a_vowel_and_shuts_on_an_m()
    {
        var open = Said("[m aa aa m]", 1, ("every", 0f));

        var widest = open.Take(Rate).Max();
        widest.ShouldBeGreaterThan(0.7);
        open[(int)(0.04 * Rate)].ShouldBeLessThan(0.2);
    }

    [Fact]
    public void Its_lips_round_on_an_oo()
    {
        Said("[uw uw]", 2, ("every", 0f)).Take(Rate).Max().ShouldBeGreaterThan(0.7);
        Said("[iy iy]", 2, ("every", 0f)).Take(Rate).Max().ShouldBeLessThan(0.2);
    }

    [Fact]
    public void Its_caption_moves_to_the_next_line_and_types_it_out()
    {
        var line = Said("Hi.\nBye.", 3, ("every", 0f));
        var reveal = Said("Hi.\nBye.", 4, ("every", 0f));

        line[(int)(0.1 * Rate)].ShouldBe(0d);
        line.Max().ShouldBe(1d);

        var second = Array.IndexOf(line, 1d);
        reveal[second + Rate / 200].ShouldBeLessThan(0.6);
        reveal.Skip(second).Max().ShouldBe(1d, 0.01);
    }

    // --- harness -----------------------------------------------------------------

    private static double[] Said(string words, params (string Port, float Value)[] knobs) => Said(words, 0, knobs);

    /// <summary>Three seconds of the Speak's output <paramref name="output"/>.</summary>
    private static double[] Said(string words, int output, params (string Port, float Value)[] knobs) =>
        Played(new JsonObject { ["words"] = words }, output, 3, knobs);

    private static double[] Sung(string words, string notes, float bpm, int seconds = 2) =>
        Played(new JsonObject { ["words"] = words, ["mode"] = "sing", ["notes"] = notes }, 0, seconds, ("bpm", bpm));

    private static double[] Played(JsonObject state, int output, int seconds, params (string Port, float Value)[] knobs)
    {
        var def = Catalog.Get(SpeakType).ShouldNotBeNull();
        var b = new PatchBuilder(Catalog);
        var speak = b.Add(SpeakType, [.. knobs.Select(k => (def.Inputs.ToList().FindIndex(p => p.Name == k.Port), k.Value))]);
        speak.SetState("speak", state);
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(speak, output, sink, NodeCatalog.OutputLeftPort);

        return Interpreted.Run(b.Patch, new float[Rate * seconds], lowered: true);
    }

    /// <summary>
    /// The pitch of the 50 ms from <paramref name="at"/>: the shortest shift at which it
    /// all but matches itself, since every whole number of periods matches as well.
    /// </summary>
    private static double Pitch(double[] signal, float at)
    {
        var span = signal.AsSpan((int)(at * Rate), Rate / 20);
        var matches = new double[Rate / 60];

        for (var lag = Rate / 500; lag < matches.Length; lag++)
        {
            for (var i = 0; i + lag < span.Length; i++) matches[lag] += span[i] * span[i + lag];
            matches[lag] /= span.Length - lag;
        }

        var best = matches.Max();
        var first = Array.FindIndex(matches, Rate / 500, m => m >= best * 0.9);

        // On to the top of that peak.
        while (first + 1 < matches.Length && matches[first + 1] > matches[first]) first++;

        return Rate / (double)first;
    }

    /// <summary>The last moment anything louder than a whisper is heard, in seconds.</summary>
    private static float End(double[] signal)
    {
        var last = Array.FindLastIndex(signal, s => Math.Abs(s) > 1e-3);
        return last / (float)Rate;
    }

    private static double Rms(double[] signal, float from, float to)
    {
        var span = signal.AsSpan((int)(from * Rate), (int)((to - from) * Rate));
        var sum = 0d;
        foreach (var sample in span) sum += sample * sample;
        return Math.Sqrt(sum / span.Length);
    }
}
