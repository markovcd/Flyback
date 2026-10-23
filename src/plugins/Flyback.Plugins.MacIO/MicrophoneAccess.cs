using System.Runtime.InteropServices;

// Names follow the native API they bind.
// ReSharper disable InconsistentNaming

namespace Flyback.Plugins.MacIO;

/// <summary>
/// Whether macOS's privacy settings let this program hear the microphone, asked of
/// <c>AVCaptureDevice</c> through the Objective-C runtime.
/// </summary>
/// <remarks>
/// A refused microphone records silence rather than failing, so this is the only way to
/// say why a Line In hears nothing. The prompt itself comes from opening the input.
/// </remarks>
internal static partial class MicrophoneAccess
{
    private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";

    private const string AVFoundation = "/System/Library/Frameworks/AVFoundation.framework/AVFoundation";

    /// <summary><c>AVAuthorizationStatusRestricted</c>: a profile or parental controls forbid it.</summary>
    private const nint Restricted = 1;

    /// <summary><c>AVAuthorizationStatusDenied</c>: the person said no.</summary>
    private const nint Denied = 2;

    /// <summary>
    /// True only where macOS says outright that the microphone is refused; not yet asked,
    /// allowed, or a question that cannot be put all read as false.
    /// </summary>
    public static bool IsRefused()
    {
        try
        {
            if (!NativeLibrary.TryLoad(AVFoundation, out var library)) return false;
            if (!NativeLibrary.TryGetExport(library, "AVMediaTypeAudio", out var audio)) return false;

            var device = GetClass("AVCaptureDevice");

            if (device == IntPtr.Zero) return false;

            var status = Send(device, RegisterSelector("authorizationStatusForMediaType:"), Marshal.ReadIntPtr(audio));

            return status is Restricted or Denied;
        }
        catch
        {
            return false;
        }
    }

    [LibraryImport(ObjectiveC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GetClass(string name);

    [LibraryImport(ObjectiveC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr RegisterSelector(string name);

    /// <summary>A message with one object argument and an integer answer.</summary>
    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(IntPtr receiver, IntPtr selector, IntPtr argument);
}
