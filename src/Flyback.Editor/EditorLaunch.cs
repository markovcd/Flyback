using Flyback.App.Updates;
using Flyback.Core.Compile;

namespace Flyback.App;

/// <summary>What this launch was asked to do, and what it has to say once the window opens.</summary>
public sealed record EditorLaunch : IIlCompilerSetup
{
    /// <summary>A file to open once there is a window for it, or null for the usual start on the default preset.</summary>
    public string? OpenPath { get; init; }

    /// <summary>The shared preset a restart was carrying, by its id on the preset site.</summary>
    public string? OpenShared { get; init; }

    /// <summary>What <see cref="Interpreted"/> is asked for with on the command line.</summary>
    public const string InterpretedFlag = "--interpreted";

    /// <summary>Keep the CPU's programs on the interpreter for the whole run.</summary>
    public bool Interpreted { get; init; }

    /// <summary>What the last update and any plugin just installed did, said once on the status bar.</summary>
    public string? OpeningNote { get; init; }

    /// <summary>
    /// What the release just installed changed, shown once in a dialog when the
    /// window opens in place of <see cref="OpeningNote"/>.
    /// </summary>
    public ReleaseNotes? WhatsNew { get; init; }
}
