using System.Text.Json.Nodes;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Voice;

/// <summary>The Speak, played from the start of the patch with its words set on the node.</summary>
public class SpeakTests
{
    private const string SpeakType = "flyback.voice.speak";

    private const int Rate = GlobalConstants.SampleRate;

    private static readonly ModuleCatalog Catalog = ShippedPlugins.Loaded.Modules;

    [Fact]
    public void The_voice_plugin_offers_it_with_its_words_on_the_node()
    {
        var def = Catalog.Get(SpeakType).ShouldNotBeNull();

        def.Name.ShouldBe("Speak");
        def.Sinks.ShouldBe(ModuleSinks.Audio);
        def.Outputs.ShouldHaveSingleItem().Name.ShouldBe("out");
        def.Extras.ShouldHaveSingleItem().Fields.Select(f => f.Key).ShouldBe(["words"]);
        Catalog.ProviderOf(SpeakType)!.Id.ShouldBe("flyback.voice");
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

    // --- harness -----------------------------------------------------------------

    private static double[] Said(string words, params (string Port, float Value)[] knobs)
    {
        var def = Catalog.Get(SpeakType).ShouldNotBeNull();
        var b = new PatchBuilder(Catalog);
        var speak = b.Add(SpeakType, [.. knobs.Select(k => (def.Inputs.ToList().FindIndex(p => p.Name == k.Port), k.Value))]);
        speak.SetState("speak", new JsonObject { ["words"] = words });
        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        b.Wire(speak, 0, sink, NodeCatalog.OutputLeftPort);

        return Interpreted.Run(b.Patch, new float[Rate * 3]);
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
