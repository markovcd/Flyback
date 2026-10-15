using System.Xml;
using System.Xml.Linq;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Specs.Support;
using Flyback.Tests;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The sound input a patch listens to, and what the speakers make of it.</summary>
[Binding]
public sealed class LineInSteps(PatchContext context)
{
    private const int Seconds = 1;

    private float[] heard = [];

    [Given("a Line In is patched into the speakers")]
    public void GivenALineIn()
    {
        context.Add("line", NodeCatalog.LineInTypeId);
        context.Add("screen", NodeCatalog.OutputTypeId);
        context.Wire("line", "left", "screen", "left");
        context.SetInput("screen", "volume", 1f);
    }

    [When("it hears a {float} Hz tone")]
    public void WhenItHears(float hertz) =>
        heard = context.RenderAudio(GlobalConstants.SampleRate * Seconds, new RecordedLineIn(Tone(hertz, GlobalConstants.SampleRate, Seconds)));

    /// <summary>
    /// The shipped plugins carry the backend, and on the system running the suite it is the
    /// one a Line In listens through wherever it can run.
    /// </summary>
    [Then("on {word} a Line In listens through {word}")]
    public static void ThenItListensThrough(string system, string backend)
    {
        var plugins = ShippedPlugins.Loaded;
        var input = plugins.AudioInputs.Where(i => i.Name.StartsWith(backend, StringComparison.Ordinal)).ShouldHaveSingleItem();

        if (OperatingSystem.IsOSPlatform(system) && input.IsSupported) plugins.PreferredAudioInput.ShouldBeSameAs(input);
    }

    /// <summary>The usage string macOS shows when it asks, without which it ends the program instead.</summary>
    [Then("the macOS app's Info.plist explains its use of the microphone")]
    public static void ThenThePlistExplains()
    {
        using var reader = XmlReader.Create(
            Path.Combine(Repository.Root, "src", "Flyback.Editor.Desktop", "Info.plist"),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });

        var entries = XDocument.Load(reader).Root!.Element("dict")!.Elements().ToList();
        var key = entries.FindIndex(entry => entry.Name == "key" && entry.Value == "NSMicrophoneUsageDescription");

        key.ShouldBeGreaterThanOrEqualTo(0);
        entries[key + 1].Name.LocalName.ShouldBe("string");
        entries[key + 1].Value.ShouldContain("Line In");
    }

    /// <summary>Whole cycles in the left speaker, counted as rising crossings of nought, past the filters' first few frames.</summary>
    [Then("the speakers play a {float} Hz tone")]
    public void ThenTheSpeakersPlay(float hertz) =>
        RisingCrossings(heard.Where((_, i) => i % 2 == 0).Skip(200).ToArray(), GlobalConstants.SampleRate)
            .ShouldBe(hertz, hertz * 0.02);

    internal static float[] Tone(float hertz, int rate, int seconds) =>
        [.. Enumerable.Range(0, rate * seconds).Select(i => 0.5f * MathF.Sin(MathF.Tau * hertz * i / rate))];

    /// <summary>The pitch of a signal in hertz, from how often it crosses nought on the way up.</summary>
    internal static double RisingCrossings(float[] signal, int rate)
    {
        var crossings = 0;

        for (var i = 1; i < signal.Length; i++)
            if (signal[i - 1] < 0f && signal[i] >= 0f)
                crossings++;

        return crossings * rate / (double)signal.Length;
    }
}
