using Flyback.Plugins.Hosting;
using Flyback.Plugins.Midi;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The half of hearing a keyboard that needs no keyboard: what three bytes off a
/// wire mean, and what a device is called once a patch has to remember it.
/// </summary>
/// <remarks>
/// Both of these live in the contract rather than in a backend, which is what
/// makes them testable at all — the alternative is a device plugged into the
/// machine running the tests, which no build server has. What is left untested
/// is the part that genuinely needs hardware: opening a port and being called
/// back on the driver's thread.
/// </remarks>
public class MidiInputTests
{
    private static PluginCatalog Shipped() => PluginHost.Load();

    [Fact]
    public void The_windows_backend_is_found_and_registers_itself()
    {
        var catalog = Shipped();

        catalog.Problems.ShouldBeEmpty();
        catalog.Plugins.Select(p => p.Info.Id).ShouldContain("win.io");
        catalog.MidiInputs.Select(i => i.Id).ShouldContain("winmm");
    }

    /// <summary>
    /// The plugin loads everywhere; only the devices are tied to one system.
    /// That split is the whole point of separating <see cref="IMidiInput"/> from
    /// <see cref="IMidiPort"/>, so it is worth pinning.
    /// </summary>
    [Fact]
    public void Support_is_answered_without_opening_a_device()
    {
        Shipped().MidiInputs.Single(i => i.Id == "winmm").IsSupported.ShouldBe(OperatingSystem.IsWindows());
    }

    /// <summary>
    /// Enumerating is a question, not an opening. It has to be total, because a
    /// picker asks it every time it is drawn and a compile asks it too — and
    /// there is always the computer's own keyboard behind whatever it says.
    /// </summary>
    [Fact]
    public void Listing_what_is_plugged_in_never_throws()
    {
        foreach (var input in Shipped().MidiInputs)
            Should.NotThrow(() => input.Ports);
    }

    /// <summary>
    /// Three backends ship and none ever competes with the others: each is
    /// supported only where the rest are not. Linux is allowed to choose nothing
    /// as well, because a machine with no libasound on it has no MIDI to offer
    /// and says so rather than failing.
    /// </summary>
    [Fact]
    public void The_backend_chosen_here_is_the_one_for_this_operating_system()
    {
        Shipped().PreferredMidiInput?.Id.ShouldBe(
            OperatingSystem.IsWindows() ? "winmm"
            : OperatingSystem.IsMacOS() ? "coremidi"
            : OperatingSystem.IsLinux() ? "alsaseq"
            : null);
    }

    /// <summary>A device that is not plugged in must be refused, not stood in for.</summary>
    [Fact]
    public void Opening_something_that_is_not_there_fails()
    {
        var input = Shipped().MidiInputs.Single(i => i.Id == "winmm");

        Assert.SkipUnless(OperatingSystem.IsWindows(), "the Windows backend only opens on Windows.");

        Should.Throw<InvalidOperationException>(
            () => input.Open("midi:no-such-device-is-plugged-in", _ => { }));
    }

    [Fact]
    public void Off_windows_the_backend_refuses_rather_than_pretending()
    {
        var input = Shipped().MidiInputs.Single(i => i.Id == "winmm");

        Assert.SkipWhen(OperatingSystem.IsWindows(), "this is the backend for this machine.");

        input.Ports.ShouldBeEmpty();
        Should.Throw<PlatformNotSupportedException>(() => input.Open("midi:anything", _ => { }));
    }

    // ---- and the same of the Linux one -----------------------------------------

    [Fact]
    public void The_linux_backend_is_found_and_registers_itself()
    {
        var catalog = Shipped();

        catalog.Problems.ShouldBeEmpty();
        catalog.Plugins.Select(p => p.Info.Id).ShouldContain("linux.io");
        catalog.MidiInputs.Select(i => i.Id).ShouldContain("alsaseq");
    }

    /// <summary>
    /// Unlike the Windows one this is not answered by the operating system
    /// alone: a container or a server install has no libasound, and the backend
    /// has to find that out without a device and without throwing. So what is
    /// pinned here is the half that is certain — off Linux it is always no.
    /// </summary>
    [Fact]
    public void Off_linux_the_alsa_backend_refuses_rather_than_pretending()
    {
        var input = Shipped().MidiInputs.Single(i => i.Id == "alsaseq");

        Assert.SkipWhen(OperatingSystem.IsLinux(), "this is the backend for this machine.");

        input.IsSupported.ShouldBeFalse();
        input.Ports.ShouldBeEmpty();
        Should.Throw<PlatformNotSupportedException>(() => input.Open("midi:anything", _ => { }));
    }

    /// <summary>A device that is not plugged in must be refused, not stood in for.</summary>
    [Fact]
    public void Opening_something_that_is_not_there_fails_on_linux_too()
    {
        var input = Shipped().MidiInputs.Single(i => i.Id == "alsaseq");

        Assert.SkipUnless(
            OperatingSystem.IsLinux() && input.IsSupported,
            "the ALSA backend only opens on Linux, and only where there is a libasound.");

        Should.Throw<InvalidOperationException>(
            () => input.Open("midi:no-such-device-is-plugged-in", _ => { }));
    }

    // ---- and the same of the macOS one -----------------------------------------

    [Fact]
    public void The_macos_backend_is_found_and_registers_itself()
    {
        var catalog = Shipped();

        catalog.Problems.ShouldBeEmpty();
        catalog.Plugins.Select(p => p.Info.Id).ShouldContain("mac.io");
        catalog.MidiInputs.Select(i => i.Id).ShouldContain("coremidi");
    }

    /// <summary>
    /// Answered by the operating system alone, like the Windows one and unlike
    /// the Linux one: CoreMIDI is part of macOS, so there is no machine that has
    /// the right system and not the framework.
    /// </summary>
    [Fact]
    public void Support_for_the_macos_backend_is_the_operating_system_and_nothing_else()
    {
        Shipped().MidiInputs.Single(i => i.Id == "coremidi").IsSupported.ShouldBe(OperatingSystem.IsMacOS());
    }

    [Fact]
    public void Off_macos_the_coremidi_backend_refuses_rather_than_pretending()
    {
        var input = Shipped().MidiInputs.Single(i => i.Id == "coremidi");

        Assert.SkipWhen(OperatingSystem.IsMacOS(), "this is the backend for this machine.");

        input.Ports.ShouldBeEmpty();
        Should.Throw<PlatformNotSupportedException>(() => input.Open("midi:anything", _ => { }));
    }

    /// <summary>A device that is not plugged in must be refused, not stood in for.</summary>
    [Fact]
    public void Opening_something_that_is_not_there_fails_on_macos_too()
    {
        var input = Shipped().MidiInputs.Single(i => i.Id == "coremidi");

        Assert.SkipUnless(OperatingSystem.IsMacOS(), "the CoreMIDI backend only opens on macOS.");

        Should.Throw<InvalidOperationException>(
            () => input.Open("midi:no-such-device-is-plugged-in", _ => { }));
    }

    // ---- what three bytes mean -------------------------------------------------

    [Fact]
    public void A_struck_key_is_a_note_going_down()
    {
        var message = MidiMessages.Of(0x90, 60, 127).ShouldNotBeNull();

        message.Action.ShouldBe(MidiAction.Down);
        message.Note.ShouldBe(60);
        message.Velocity.ShouldBe(1f);
    }

    /// <summary>
    /// The one that catches out everybody who writes this from the specification
    /// rather than from a device: a great many keyboards never send a note-off at
    /// all, and say it with a note-on of nought instead.
    /// </summary>
    [Fact]
    public void A_note_on_with_no_force_behind_it_is_a_note_let_go()
    {
        var message = MidiMessages.Of(0x90, 60, 0).ShouldNotBeNull();

        message.Action.ShouldBe(MidiAction.Up);
        message.Note.ShouldBe(60);
    }

    [Fact]
    public void A_note_off_is_a_note_let_go()
    {
        MidiMessages.Of(0x80, 64, 64).ShouldNotBeNull().Action.ShouldBe(MidiAction.Up);
    }

    /// <summary>Velocity arrives as 0 to 127 and leaves as 0 to 1, once, here.</summary>
    [Theory]
    [InlineData(127, 1f)]
    [InlineData(64, 64f / 127f)]
    [InlineData(1, 1f / 127f)]
    public void Velocity_is_divided_out_of_the_wire(byte sent, float expected)
    {
        MidiMessages.Of(0x90, 60, sent).ShouldNotBeNull().Velocity.ShouldBe(expected, 0.0001f);
    }

    /// <summary>
    /// A keyboard split across channels is still one pair of hands, and the
    /// module has nowhere to name a channel — so all sixteen play the one voice.
    /// </summary>
    [Theory]
    [InlineData(0x90)]
    [InlineData(0x95)]
    [InlineData(0x9F)]
    public void Every_channel_plays(byte status)
    {
        MidiMessages.Of(status, 60, 100).ShouldNotBeNull().Action.ShouldBe(MidiAction.Down);
    }

    [Theory]
    [InlineData(120)] // all sound off
    [InlineData(123)] // all notes off
    public void The_panic_buttons_let_everything_go(byte controller)
    {
        MidiMessages.Of(0xB0, controller, 0).ShouldNotBeNull().Action.ShouldBe(MidiAction.AllOff);
    }

    /// <summary>
    /// Everything a cable carries that this does not read. Clock at 0xF8 is the
    /// one that matters most: it arrives twenty-four times a beat, and a decoder
    /// that read its top nibble as a command would call every tick a note.
    /// </summary>
    [Theory]
    [InlineData(0xF8, 0, 0)]     // clock
    [InlineData(0xFE, 0, 0)]     // active sensing
    [InlineData(0xF0, 0x7E, 0)]  // the start of a system-exclusive conversation
    [InlineData(0xB0, 1, 64)]    // the modulation wheel
    [InlineData(0xE0, 0, 64)]    // pitch bend
    [InlineData(0xD0, 64, 0)]    // channel pressure
    [InlineData(0xC0, 5, 0)]     // a program change
    [InlineData(0x40, 60, 100)]  // a data byte where a status byte should be
    public void Everything_else_means_nothing_to_a_voice(byte status, byte first, byte second)
    {
        MidiMessages.Of(status, first, second).ShouldBeNull();
    }

    // ---- what a device is called ----------------------------------------------

    [Fact]
    public void A_device_is_named_by_what_it_is_called_rather_than_where_it_is_plugged()
    {
        var ports = MidiPorts.Named(["Launchkey Mini MK3"]);

        ports.Single().Id.ShouldBe("midi:launchkey-mini-mk3");
        ports.Single().Name.ShouldBe("Launchkey Mini MK3");
    }

    /// <summary>
    /// The prefix earns its place here: without it a device called "Keyboard"
    /// would take the id of the computer's own keys, and the one instrument that
    /// is always there would become unreachable.
    /// </summary>
    [Fact]
    public void A_device_can_never_take_the_computer_keyboards_id()
    {
        MidiPorts.Named(["Keyboard"]).Single().Id.ShouldNotBe("keyboard");
    }

    [Theory]
    [InlineData("Launchkey  Mini  MK3", "midi:launchkey-mini-mk3")]
    [InlineData("Launchkey Mini [MK3]", "midi:launchkey-mini-mk3")]
    [InlineData("  MPK mini  ", "midi:mpk-mini")]
    [InlineData("2- USB MIDI Interface", "midi:2-usb-midi-interface")]
    public void Punctuation_and_spacing_do_not_make_a_second_device(string name, string id)
    {
        MidiPorts.Named([name]).Single().Id.ShouldBe(id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void A_device_with_nothing_usable_in_its_name_still_gets_one(string name)
    {
        var port = MidiPorts.Named([name]).Single();

        port.Id.ShouldStartWith(MidiPorts.Prefix);
        port.Id.Length.ShouldBeGreaterThan(MidiPorts.Prefix.Length);
        port.Name.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Two of the same model is the case with no good answer, so pin the one it
    /// gives: both are reachable, they differ, and the picker says which is which.
    /// </summary>
    [Fact]
    public void Two_of_the_same_model_are_told_apart()
    {
        var ports = MidiPorts.Named(["MPK mini", "MPK mini", "MPK mini"]);

        ports.Select(p => p.Id).ShouldBeUnique();
        ports[0].Id.ShouldBe("midi:mpk-mini");
        ports[1].Id.ShouldBe("midi:mpk-mini-2");
        ports[2].Id.ShouldBe("midi:mpk-mini-3");

        ports[1].Name.ShouldBe("MPK mini (2)");
    }

    /// <summary>
    /// The order is what turns an id back into the number a backend opens, so it
    /// has to be the order it was given in and not a sorted one.
    /// </summary>
    [Fact]
    public void The_order_is_the_one_the_machine_gave()
    {
        MidiPorts.Named(["Zebra", "Apple", "Mango"])
            .Select(p => p.Name)
            .ShouldBe(["Zebra", "Apple", "Mango"]);
    }
}
