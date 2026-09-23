using Flyback.App.Midi;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Midi;

/// <summary>
/// What Flyback knows about an instrument by name, from a file: which port is it,
/// which channel is which track, which controller is which knob, and what to call
/// a binding once it knows.
/// </summary>
/// <remarks>
/// Here rather than in the specs because a profile is the shell's: the engine
/// stores a channel and a controller number and never sees a name for either.
/// </remarks>
public class InstrumentProfileTests
{
    private static InstrumentProfile Syntakt() =>
        InstrumentLibrary.Shipped().Profiles.Single(profile => profile.Name == "Syntakt");

    [Fact]
    public void The_syntakt_ships_with_its_tracks_and_its_fx_track()
    {
        var syntakt = Syntakt();

        syntakt.Conducts.ShouldBeTrue();
        syntakt.Tracks.Count.ShouldBe(13);
        syntakt.Track(1)!.Name.ShouldBe("Track 1");
        syntakt.Track(13)!.Kind.ShouldBe("fx");
        syntakt.Track(14).ShouldBeNull();
    }

    [Theory]
    [InlineData("midi:elektron-syntakt", "Elektron Syntakt")]
    [InlineData("midi:syntakt", "SYNTAKT")]
    [InlineData("midi:usb-midi-2", "Syntakt Port 1")]
    public void The_syntakt_is_known_by_its_port_name_or_id(string id, string name)
    {
        InstrumentLibrary.Shipped().For(id, name).ShouldNotBeNull().Name.ShouldBe("Syntakt");
    }

    [Fact]
    public void A_device_flyback_has_never_heard_of_has_no_profile()
    {
        InstrumentLibrary.Shipped().For("midi:launchkey-mini", "Launchkey Mini").ShouldBeNull();
    }

    /// <summary>The whole point: a binding reads as the box's own words rather than as a number.</summary>
    [Fact]
    public void A_binding_is_described_by_track_and_knob()
    {
        Syntakt().Describe(new MidiBinding("midi:syntakt", 3, 74)).ShouldBe("Syntakt · Track 3 · Filter Frequency");
    }

    /// <summary>Under a knob there is room for the track's short name and the knob, and no more.</summary>
    [Fact]
    public void A_binding_has_a_short_form_for_under_a_knob()
    {
        var syntakt = Syntakt();

        syntakt.Label(new MidiBinding("midi:syntakt", 3, 74)).ShouldBe("T3 · Filter Frequency");
        syntakt.Label(new MidiBinding("midi:syntakt", 13, 70)).ShouldBe("FX · FX filter Frequency");
        syntakt.Label(new MidiBinding("midi:syntakt", 15, 74)).ShouldBe("ch 15 · CC74");
        syntakt.Label(new MidiBinding("midi:syntakt", 0, 74)).ShouldBe("CC74");
        InstrumentLibrary.Shipped().Label(new MidiBinding("midi:other", 2, 21), new MidiSource("midi:other", "Other")).ShouldBe("CC21·2");
    }

    /// <summary>
    /// The same controller number means a different knob on the FX track, so the
    /// track's kind decides which pages are read.
    /// </summary>
    [Fact]
    public void The_fx_track_has_its_own_pages()
    {
        var syntakt = Syntakt();

        syntakt.Describe(new MidiBinding("midi:syntakt", 13, 70)).ShouldBe("Syntakt · FX track · FX filter Frequency");
        syntakt.Describe(new MidiBinding("midi:syntakt", 1, 70)).ShouldBe("Syntakt · Track 1 · Filter Attack");
        syntakt.PagesOf(syntakt.Track(13)!).Select(page => page.Name).ShouldNotContain("Syn");
        syntakt.PagesOf(syntakt.Track(1)!).Select(page => page.Name).ShouldNotContain("Delay");
    }

    [Fact]
    public void What_the_profile_has_no_name_for_is_still_said_as_far_as_it_goes()
    {
        var syntakt = Syntakt();

        syntakt.Describe(new MidiBinding("midi:syntakt", 3, 1)).ShouldBe("Syntakt · Track 3 · CC1");
        syntakt.Describe(new MidiBinding("midi:syntakt", 15, 74)).ShouldBe("Syntakt · channel 15 · CC74");
        syntakt.Describe(new MidiBinding("midi:syntakt", 0, 74)).ShouldBe("Syntakt · CC74");
    }

    [Fact]
    public void A_binding_to_an_unknown_device_keeps_its_plain_label()
    {
        var library = InstrumentLibrary.Shipped();
        var binding = new MidiBinding("midi:launchkey-mini", 2, 21);

        library.Describe(binding, new MidiSource("midi:launchkey-mini", "Launchkey Mini")).ShouldBe("CC21·2");
        library.Describe(binding, null).ShouldBe("CC21·2");
    }

    /// <summary>A box set up on other channels is a file in the user's folder, which replaces the shipped one by name.</summary>
    [Fact]
    public void A_profile_in_the_users_folder_replaces_the_shipped_one_of_the_same_name()
    {
        var folder = Path.Combine(Path.GetTempPath(), "flyback-instruments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllText(Path.Combine(folder, "mine.json"), """
                {
                  "name": "syntakt",
                  "matches": ["syntakt"],
                  "tracks": [ { "name": "Kick", "channel": 5 } ],
                  "pages": [ { "name": "Filter", "controls": { "Cutoff": 74 } } ]
                }
                """);
            File.WriteAllText(Path.Combine(folder, "broken.json"), "{ this is not json");
            File.WriteAllText(Path.Combine(folder, "nameless.json"), """{ "tracks": [] }""");

            var library = InstrumentLibrary.Load(folder);

            library.Profiles.Count(profile => profile.Name.Equals("syntakt", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
            library.For("midi:syntakt", "Syntakt")!.Describe(new MidiBinding("midi:syntakt", 5, 74)).ShouldBe("syntakt · Kick · Filter Cutoff");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_missing_folder_is_only_the_shipped_profiles()
    {
        InstrumentLibrary.Load(Path.Combine(Path.GetTempPath(), "flyback-nowhere-" + Guid.NewGuid().ToString("N")))
            .Profiles.Select(profile => profile.Name).ShouldBe(InstrumentLibrary.Shipped().Profiles.Select(profile => profile.Name));
    }

    /// <summary>A profile only keeps what a binding could hold: a channel and a controller number each in range.</summary>
    [Fact]
    public void Out_of_range_tracks_and_controls_are_left_out()
    {
        var profile = InstrumentLibrary.Read("""
            {
              "name": "Box",
              "tracks": [ { "name": "A", "channel": 0 }, { "name": "B", "channel": 17 }, { "name": "C", "channel": 16 } ],
              "pages": [ { "name": "P", "controls": { "Low": -1, "High": 128, "Fine": 127 } } ]
            }
            """).ShouldNotBeNull();

        profile.Tracks.Select(track => track.Name).ShouldBe(["C"]);
        profile.Pages.Single().Controls.Select(control => control.Name).ShouldBe(["Fine"]);
    }

    /// <summary>A fresh Clock In follows the box that conducts, whichever socket it is in.</summary>
    [Fact]
    public void A_fresh_clock_in_follows_the_instrument_that_conducts()
    {
        try
        {
            MidiSources.Install(() =>
            [
                new MidiSource(MidiSources.Keyboard, "Computer keyboard"),
                new MidiSource("midi:launchkey-mini", "Launchkey Mini"),
                new MidiSource("midi:syntakt", "Syntakt") { Conducts = true },
            ]);

            var field = new MidiClockExtra().Fields.OfType<Flyback.Core.Graph.ExtraField.Choice>().Single();

            field.Fallback.ShouldBe("midi:syntakt");
        }
        finally
        {
            MidiSources.Install(() => [new MidiSource(MidiSources.Keyboard, "Computer keyboard")]);
        }
    }
}
