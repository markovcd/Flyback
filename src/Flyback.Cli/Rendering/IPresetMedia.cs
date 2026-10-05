namespace Flyback.Cli.Rendering;

/// <summary>Where a preset's render goes: the site's media folder, or the site itself through its admin API.</summary>
internal interface IPresetMedia
{
    /// <summary>Whether the preset has neither its media nor a failed render yet.</summary>
    bool Pending(string id);

    /// <summary>Puts the file <paramref name="from"/> as the preset's file with <paramref name="suffix"/>, such as <c>.webp</c>.</summary>
    Task Put(string id, string suffix, string from, CancellationToken cancellation);

    /// <summary>Says the rest is in place. Sent last, so the site never shows half a render.</summary>
    Task Done(string id, CancellationToken cancellation);

    Task Failed(string id, string why, CancellationToken cancellation);
}
