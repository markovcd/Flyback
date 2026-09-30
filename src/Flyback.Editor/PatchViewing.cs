using Flyback.App.Notices;

namespace Flyback.App;

/// <summary>Hands the viewer the patch as it stands, and pauses the editor so only one of them is heard.</summary>
internal sealed class PatchViewing(Playback playback, PatchFiles files, IViewer viewer, ReportLine report) : IReactTo<ViewAsked>
{
    public Task On(ViewAsked notice)
    {
        playback.Pause();
        viewer.Show(files.Name ?? "Untitled", Pack);

        return Task.CompletedTask;
    }

    private byte[] Pack()
    {
        using var packed = new MemoryStream();
        var packing = files.Pack(packed);

        if (!packing.Whole)
            report.Say($"Viewing it without {packing.Missing.Count} file(s) that could not be read.", string.Join(Environment.NewLine, packing.Missing));

        return packed.ToArray();
    }
}
