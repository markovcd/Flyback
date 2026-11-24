using System.Runtime.InteropServices;

namespace Flyback.Plugins.Ndi;

/// <summary>NDI's <c>NDIlib_send_create_t</c>: what a sender is called and whether NDI paces it.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NdiSendCreate
{
    /// <summary>The name the sender is seen by, UTF-8.</summary>
    public IntPtr Name;

    /// <summary>The groups it is seen in, or null for the default one.</summary>
    public IntPtr Groups;

    /// <summary>Whether NDI holds each frame back to the frame rate. Off: Flyback sends at its own pace.</summary>
    public byte ClockVideo;

    /// <summary>Whether NDI holds each block of sound back to the sample rate.</summary>
    public byte ClockAudio;
}
