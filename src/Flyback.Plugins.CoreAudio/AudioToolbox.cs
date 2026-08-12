using System.Runtime.InteropServices;

namespace Flyback.Plugins.CoreAudio;

/// <summary>
/// The slice of Apple's Audio Toolbox this plugin needs, and nothing else.
/// Hand-written rather than taken from a binding package, because the whole
/// surface is eight entry points and three structs — a dependency here would
/// cost more than it saved.
/// </summary>
/// <remarks>
/// Every entry point is resolved lazily by the runtime, on first call. Nothing
/// in this file runs while the plugin is merely being listed, which is what
/// lets the assembly load on Windows and answer "not supported" rather than
/// failing to load at all.
/// </remarks>
internal static partial class AudioToolbox
{
    /// <summary>
    /// The full framework path rather than a bare name: <c>dlopen</c> resolves
    /// frameworks by path, and the probing the runtime does for a plain name
    /// would not find this one.
    /// </summary>
    private const string Library = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    /// <summary>Success. Every entry point here returns an <c>OSStatus</c>.</summary>
    public const int NoError = 0;

    // Four-character codes, in the order the bytes appear in the name.
    public const uint OutputComponentType = 0x6175_6F75;   // 'auou'
    public const uint DefaultOutputSubType = 0x6465_6620;  // 'def '
    public const uint AppleManufacturer = 0x6170_706C;     // 'appl'
    public const uint LinearPcmFormat = 0x6C70_636D;       // 'lpcm'
    public const uint BufferFrameSizeProperty = 0x6673_697A; // 'fsiz'

    public const uint StreamFormatProperty = 8;
    public const uint SetRenderCallbackProperty = 23;

    public const uint GlobalScope = 0;
    public const uint InputScope = 1;

    /// <summary>
    /// <c>kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked</c>. Interleaved is
    /// the absence of a flag, and the interleaved layout is what
    /// <see cref="Flyback.Plugins.Audio.AudioCallback"/> is defined in terms of.
    /// </summary>
    public const uint FloatPacked = 1 | 8;

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioComponentDescription
    {
        public uint ComponentType;
        public uint ComponentSubType;
        public uint ComponentManufacturer;
        public uint ComponentFlags;
        public uint ComponentFlagsMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioStreamBasicDescription
    {
        public double SampleRate;
        public uint FormatId;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint ChannelsPerFrame;
        public uint BitsPerChannel;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioBuffer
    {
        public uint NumberChannels;
        public uint DataByteSize;
        public IntPtr Data;
    }

    /// <summary>
    /// C declares the tail as <c>AudioBuffer mBuffers[1]</c>, a variable-length
    /// array. An interleaved format asks for exactly one buffer, so the first
    /// one is the only one, and naming it is honest about what this code reads.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioBufferList
    {
        public uint NumberBuffers;
        public AudioBuffer FirstBuffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RenderCallback
    {
        public IntPtr InputProc;
        public IntPtr InputProcRefCon;
    }

    [LibraryImport(Library)]
    public static partial IntPtr AudioComponentFindNext(IntPtr component, in AudioComponentDescription description);

    [LibraryImport(Library)]
    public static partial int AudioComponentInstanceNew(IntPtr component, out IntPtr instance);

    [LibraryImport(Library)]
    public static partial int AudioComponentInstanceDispose(IntPtr instance);

    [LibraryImport(Library)]
    public static partial int AudioUnitSetProperty(
        IntPtr unit, uint id, uint scope, uint element, in AudioStreamBasicDescription data, uint size);

    [LibraryImport(Library)]
    public static partial int AudioUnitSetProperty(
        IntPtr unit, uint id, uint scope, uint element, in RenderCallback data, uint size);

    [LibraryImport(Library)]
    public static partial int AudioUnitSetProperty(
        IntPtr unit, uint id, uint scope, uint element, in uint data, uint size);

    [LibraryImport(Library)]
    public static partial int AudioUnitInitialize(IntPtr unit);

    [LibraryImport(Library)]
    public static partial int AudioUnitUninitialize(IntPtr unit);

    [LibraryImport(Library)]
    public static partial int AudioOutputUnitStart(IntPtr unit);

    [LibraryImport(Library)]
    public static partial int AudioOutputUnitStop(IntPtr unit);
}
