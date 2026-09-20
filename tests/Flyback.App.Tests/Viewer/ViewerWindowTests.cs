using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.App.Controls;
using Flyback.App.Tests.Ui;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Audio;
using Flyback.Viewer;
using Shouldly;

namespace Flyback.App.Tests.Viewer;

/// <summary>
/// The player in a window, driven headless: what it opens with, what the transport
/// does, and how the toolbar finds the pointer.
/// </summary>
public class ViewerWindowTests : UiTest
{
    /// <summary>A sound card the test drives: a buffer is made when the test asks for one.</summary>
    private sealed class Loopback : IAudioDevice
    {
        private AudioCallback? fill;

        public int SampleRate => GlobalConstants.SampleRate;

        public bool IsRunning => fill is not null;

        public void Start(AudioCallback callback) => fill = callback;

        public void Stop() => fill = null;

        public void Dispose() => Stop();

        public float[] Pump(int frames = 512)
        {
            var buffer = new float[frames * 2];
            (fill ?? throw new InvalidOperationException("nothing started the device"))(buffer);

            return buffer;
        }
    }

    private static Opened Plasma() => Files(Presets.All.Single(p => p.Name == "Plasma").Build(NodeCatalog.BuiltIn));

    private static Opened Files(Patch patch) => new(patch, new SampleLibrary(), new ImageLibrary());

    /// <summary>A sine into the speakers at the Output's own Volume.</summary>
    private static Patch Tone()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var time = builder.Add("time", 0, 0);
        var osc = builder.Add("osc.sine", 0, 0, (1, 220f));
        var speaker = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));

        return builder
            .Wire(time, 0, osc, 0)
            .Wire(osc, 0, speaker, NodeCatalog.OutputLeftPort)
            .Patch;
    }

    /// <summary>Software drawing, since headless has no graphics card.</summary>
    private static ViewerOptions Options() => new() { Gpu = false, Size = new PixelSize(320, 180) };

    private ViewerWindow Open(Opened opened, ViewerOptions options, IAudioDevice? device = null)
    {
        var window = Owned(new ViewerWindow(opened, device, options));

        window.Show();
        Settle(window);

        return window;
    }

    private static float Loudest(float[] buffer) => buffer.Max(MathF.Abs);

    [AvaloniaFact]
    public void A_patch_opens_drawing_its_picture()
    {
        var window = Open(Plasma(), Options());

        ReferenceEquals(window.Preview.Program, CompiledPatch.Black).ShouldBeFalse();
        window.Preview.Resolution.ShouldBe(new PixelSize(320, 180));
        window.Player.Paused.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void No_video_leaves_the_preview_with_no_program()
    {
        var window = Open(Plasma(), Options() with { NoVideo = true });

        ReferenceEquals(window.Preview.Program, CompiledPatch.Black).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_hidden_run_draws_nothing_even_when_handed_a_surface()
    {
        var preview = new PreviewHost();

        using var player = new ViewerPlayer(Plasma(), null, Options() with { Hidden = true }, preview);

        ReferenceEquals(preview.Program, CompiledPatch.Black).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void From_starts_the_picture_there()
    {
        var window = Open(Plasma(), Options() with { From = 30 });

        window.Preview.Time.ShouldBeGreaterThanOrEqualTo(30);
    }

    [AvaloniaFact]
    public void Paused_holds_the_first_frame_and_the_clock_does_not_move()
    {
        var window = Open(Plasma(), Options() with { Paused = true, From = 3 });

        window.Player.Paused.ShouldBeTrue();
        window.Preview.Clock.ShouldNotBeNull();
        window.Preview.Clock!().ShouldBe(3);

        Settle(window);

        window.Preview.Time.ShouldBe(3);
        window.Preview.Clock!().ShouldBe(3);
    }

    [AvaloniaFact]
    public void Rewinding_while_paused_is_not_undone_by_the_next_tick()
    {
        var window = Open(Plasma(), Options() with { Paused = true, From = 3 });

        window.Player.Rewind();
        Settle(window);

        window.Preview.Time.ShouldBe(0);
        window.Preview.Clock!().ShouldBe(0);
        window.Player.Paused.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_patch_with_knobs_arrives_seeded_in_both_blocks()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = builder.Add(NodeCatalog.OutputTypeId, 700, 40, (NodeCatalog.OutputVolumePort, 1f));
        var value = builder.Add("value", 200, 40, (0, 0f));

        builder.Wire(value, 0, output, NodeCatalog.OutputColorPort);
        builder.Wire(value, 0, output, NodeCatalog.OutputLeftPort);

        var patch = builder.Patch;
        var knob = patch.AddControl("Glow", 0.3f);

        ControlMap.Link(value, 0, new ControlLink(knob.Id, 0f, 1f));

        var window = Open(Files(patch), Options());

        var picture = window.Preview.Live;
        var sound = window.Player.Audio.Live;

        picture.At(picture.Keys.ToList().IndexOf(knob.Key)).ShouldBe(0.3, 1e-6);
        sound.At(sound.Keys.ToList().IndexOf(knob.Key)).ShouldBe(0.3, 1e-6);
    }

    [AvaloniaFact]
    public void The_sound_starts_with_the_window_and_the_picture_follows_its_clock()
    {
        var device = new Loopback();
        var window = Open(Files(Tone()), Options(), device);

        device.IsRunning.ShouldBeTrue();
        window.Preview.Clock.ShouldNotBeNull();

        Loudest(device.Pump()).ShouldBeGreaterThan(0.1f);
        window.Preview.Clock!().ShouldBeGreaterThan(0);
    }

    [AvaloniaFact]
    public void Pausing_stops_the_device_and_playing_starts_it_again()
    {
        var device = new Loopback();
        var window = Open(Files(Tone()), Options(), device);

        window.Player.Pause();
        device.IsRunning.ShouldBeFalse();

        window.Player.Resume();
        device.IsRunning.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void No_audio_opens_no_device_and_the_picture_runs_on_its_own_clock()
    {
        var device = new Loopback();
        var window = Open(Files(Tone()), Options() with { NoAudio = true }, device);

        device.IsRunning.ShouldBeFalse();
        window.Preview.Clock.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Mute_still_runs_the_device_so_the_clock_does_not_drift()
    {
        var device = new Loopback();
        var window = Open(Files(Tone()), Options() with { Mute = true }, device);

        device.IsRunning.ShouldBeTrue();
        Loudest(device.Pump()).ShouldBe(0f);

        window.Player.Mute(false);
        Loudest(device.Pump()).ShouldBeGreaterThan(0.1f);
    }

    [AvaloniaFact]
    public void Volume_is_how_loud_against_what_the_patch_made()
    {
        var full = new Loopback();
        Open(Files(Tone()), Options(), full);
        var loud = Loudest(full.Pump());

        var half = new Loopback();
        Open(Files(Tone()), Options() with { Volume = 0.5f }, half);
        var quiet = Loudest(half.Pump());

        quiet.ShouldBe(loud * 0.5f, 1e-4f);
    }

    [AvaloniaFact]
    public void From_seeks_the_sound_too()
    {
        var device = new Loopback();
        var window = Open(Files(Tone()), Options() with { From = 5 }, device);

        device.Pump(GlobalConstants.SampleRate / 10);

        window.Player.Audio.Time.ShouldBeGreaterThanOrEqualTo(5.0);
        window.Player.Audio.Time.ShouldBeLessThan(5.5);
    }

    [AvaloniaFact]
    public void Rewind_takes_the_sound_back_to_nought()
    {
        var device = new Loopback();
        var window = Open(Files(Tone()), Options(), device);

        device.Pump(GlobalConstants.SampleRate / 2);
        window.Player.Audio.Time.ShouldBeGreaterThan(0.4);

        window.Player.Rewind();
        device.Pump(1024);

        window.Player.Audio.Time.ShouldBeLessThan(0.1);
    }

    [AvaloniaFact]
    public void Double_click_takes_the_screen_and_escape_gives_it_back()
    {
        var window = Open(Plasma(), Options());

        var centre = window.Preview.TranslatePoint(
            new Point(window.Preview.Bounds.Width / 2, window.Preview.Bounds.Height / 2), window)!.Value;

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Settle(window);

        window.WindowState.ShouldBe(WindowState.FullScreen);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Settle(window);

        window.WindowState.ShouldBe(WindowState.Normal);
    }

    [AvaloniaFact]
    public void The_dots_come_up_as_the_pointer_comes_near_and_fall_back_when_it_leaves()
    {
        var window = Open(Plasma(), Options());

        Point At(double away)
        {
            var dots = window.Dots;
            var centre = dots.TranslatePoint(new Point(dots.Bounds.Width / 2, dots.Bounds.Height / 2), window)!.Value;

            return new Point(centre.X - away, centre.Y);
        }

        window.MouseMove(At(600));
        var far = window.DotsOpacity;

        window.MouseMove(At(150));
        var middle = window.DotsOpacity;

        window.MouseMove(At(0));
        var near = window.DotsOpacity;

        far.ShouldBeLessThan(0.1);
        middle.ShouldBeGreaterThan(far);
        near.ShouldBeGreaterThan(middle);
        near.ShouldBe(1.0, 1e-6);
    }

    [AvaloniaFact]
    public void The_curve_is_a_floor_a_ceiling_and_a_square_between()
    {
        ViewerWindow.Proximity(1000).ShouldBe(0.06, 1e-9);
        ViewerWindow.Proximity(0).ShouldBe(1.0, 1e-9);
        ViewerWindow.Proximity(150).ShouldBeLessThan(0.5);
    }

    [AvaloniaFact]
    public void No_overlay_leaves_nothing_of_the_viewer_in_the_picture()
    {
        var window = Open(Plasma(), Options() with { NoOverlay = true });

        All<Button>(window).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Background_opens_without_taking_focus()
    {
        var window = Open(Plasma(), Options() with { Background = true });

        window.ShowActivated.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_window_takes_its_own_size_apart_from_the_picture()
    {
        var window = Open(Plasma(), Options() with { Window = new PixelSize(700, 400) });

        window.Width.ShouldBe(700);
        window.Height.ShouldBe(400);
        window.Preview.Resolution.ShouldBe(new PixelSize(320, 180));
    }
}
