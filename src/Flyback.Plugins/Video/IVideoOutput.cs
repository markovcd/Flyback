using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Video;

/// <summary>
/// Somewhere a plugin sends the picture to, such as another program on the same
/// machine or the network, before anything is open. The user turns one on; none is
/// chosen for them.
/// </summary>
public interface IVideoOutput
{
    /// <summary>Stable identifier, e.g. <c>ndi</c>.</summary>
    string Id { get; }

    /// <summary>What a person should see, e.g. <c>NDI (network)</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Whether this output can run here at all, answered without opening anything
    /// and without throwing: one for another operating system, or one whose runtime
    /// is not installed, reports <c>false</c>.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>What this output lets somebody set, such as the name it is seen by, asked for again after every change (ADR-0085).</summary>
    /// <param name="values">What the form holds now, as the settings file keeps it.</param>
    IReadOnlyList<SettingField> Form(SettingValues values) => [];

    /// <summary>Opens the output for frames of <paramref name="format"/>.</summary>
    /// <param name="settings">What <see cref="Form"/> was last answered with.</param>
    IVideoFeed Create(VideoFormat format, SettingValues settings);
}
