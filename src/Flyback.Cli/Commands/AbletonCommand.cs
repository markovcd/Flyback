using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Host.Ableton;

namespace Flyback.Cli.Commands;

/// <summary>
/// Joins the Ableton Link session on the network for a while and says what it
/// sees twice a second: the session, how many other apps are in it, its tempo
/// and its beat. How a script or an agent checks that Flyback and another app
/// keep time together.
/// </summary>
internal static class AbletonCommand
{
    private const double Longest = 600d;

    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(500);

    public static Command Build(Option<bool> json)
    {
        var seconds = new Option<double>("--seconds")
        {
            Description = $"How long to stay in the session, up to {Longest:0}.",
            DefaultValueFactory = _ => 5d,
        };

        var command = new Command("ableton", "Join the Ableton Link session on the network and say what it plays.")
        {
            seconds, json,
        };

        command.SetAction((result, cancellation) =>
        {
            var length = result.GetValue(seconds);

            if (!(length is > 0d and <= Longest))
            {
                result.InvocationConfiguration.Error.WriteLine($"--seconds takes more than 0 and up to {Longest:0}.");
                return Task.FromResult(Exit.Failed);
            }

            return Run(new AbletonHub(), TimeSpan.FromSeconds(length), result.GetValue(json), result.InvocationConfiguration.Output, result.InvocationConfiguration.Error, cancellation);
        });

        return command;
    }

    public static async Task<int> Run(AbletonHub hub, TimeSpan length, bool json, TextWriter output, TextWriter error, CancellationToken cancellation)
    {
        string? trouble = null;
        hub.Trouble += message => trouble = message;

        if (!AbletonHub.Possible)
        {
            error.WriteLine("Ableton Link needs a network this platform does not give.");
            return Exit.Failed;
        }

        using (hub)
        {
            hub.Join();

            if (trouble is not null)
            {
                error.WriteLine(trouble);
                return Exit.Failed;
            }

            var samples = new List<(double At, AbletonView View)>();
            var started = DateTime.UtcNow;

            for (var at = TimeSpan.Zero; at <= length && !cancellation.IsCancellationRequested; at += Every)
            {
                try
                {
                    await Task.Delay(at == TimeSpan.Zero ? TimeSpan.FromMilliseconds(50) : Every, cancellation);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var view = hub.Look();
                var since = (DateTime.UtcNow - started).TotalSeconds;

                samples.Add((since, view));

                if (!json)
                    output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{since,6:0.0}s  session {view.Session}  {view.Peers} other app{(view.Peers == 1 ? "" : "s")}  {view.Bpm,7:0.00} bpm  beat {view.Beat,9:0.000}  {(view.Running ? "running" : "stopped")}"));
            }

            if (json)
            {
                output.WriteLine(JsonSerializer.Serialize(
                    new
                    {
                        samples = samples.Select(sample => new
                        {
                            at = Math.Round(sample.At, 3),
                            session = sample.View.Session,
                            peers = sample.View.Peers,
                            bpm = Math.Round(sample.View.Bpm, 3),
                            beat = Math.Round(sample.View.Beat, 3),
                            running = sample.View.Running,
                        }),
                    },
                    Writing.Json));
            }
        }

        return Exit.Ok;
    }
}
