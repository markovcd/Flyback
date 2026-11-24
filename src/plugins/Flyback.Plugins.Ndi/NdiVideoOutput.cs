using Flyback.Plugins.Settings;
using Flyback.Plugins.Video;

namespace Flyback.Plugins.Ndi;

/// <summary>The picture sent over the network as an NDI source, for VJ software on this machine or another.</summary>
internal sealed class NdiVideoOutput : IVideoOutput
{
    public const string NameKey = "name";

    public const string DefaultName = "Flyback";

    public string Id => "ndi";

    public string Name => "NDI (network)";

    /// <summary>Whether NDI's runtime is installed where its installers put it; nothing is loaded to answer.</summary>
    public bool IsSupported
    {
        get
        {
            try
            {
                return NdiRuntime.Find() is not null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        }
    }

    public IReadOnlyList<SettingField> Form(SettingValues values) =>
    [
        new SettingField.Text(NameKey, "Seen as", DefaultName, DefaultName)
        {
            Note = "The name VJ software lists this source by, after the computer's name.",
        },
    ];

    public IVideoFeed Create(VideoFormat format, SettingValues settings) =>
        new NdiFeed(NdiRuntime.Open(), format, settings.Text(NameKey, DefaultName));
}
