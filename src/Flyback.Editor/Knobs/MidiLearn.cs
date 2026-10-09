using Flyback.Core.Graph;
using Flyback.Ui.Midi;

namespace Flyback.Editor.Knobs;

/// <summary>The one learn under way for a panel: starting another gives way to it.</summary>
internal sealed class MidiLearn(MidiHub midi, ReportLine report)
{
    private Run? current;

    /// <summary>A learn is waiting on a controller.</summary>
    public bool Active => current is not null;

    /// <summary>Stops the learn under way, if any.</summary>
    public void Cancel() => current?.Source.Cancel();

    /// <summary>Gives way to any learn under way and begins one, or says no device is plugged in and returns null.</summary>
    public Run? Start(string noDevice)
    {
        Cancel();

        var devices = midi.Sources.Select(source => source.Id).Where(id => id != MidiSources.Keyboard).ToList();

        if (devices.Count == 0)
        {
            report.Say(noDevice);
            return null;
        }

        return current = new Run(this, devices);
    }

    /// <summary>One learn: the controllers it listens to and the token that stops it.</summary>
    internal sealed class Run
    {
        private readonly MidiLearn owner;

        internal Run(MidiLearn owner, List<string> devices)
        {
            this.owner = owner;
            Devices = devices;
        }

        internal CancellationTokenSource Source { get; } = new();

        public IReadOnlyList<string> Devices { get; }

        public CancellationToken Token => Source.Token;

        /// <summary>Ends this learn, and says whether it was still the one under way rather than one that gave way.</summary>
        public bool End()
        {
            var was = owner.current == this;

            if (was) owner.current = null;

            Source.Dispose();
            return was;
        }
    }
}
