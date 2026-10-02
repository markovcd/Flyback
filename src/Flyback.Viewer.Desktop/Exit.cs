namespace Flyback.Viewer.Desktop;

/// <summary>What the program tells the shell it did.</summary>
internal static class Exit
{
    public const int Ok = 0;

    /// <summary>There was nothing to play: no such file, a patch that does not read, a name no preset has.</summary>
    public const int Failed = 2;
}