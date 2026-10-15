using System.Runtime.InteropServices;

namespace Flyback.Plugins.MacIO;

/// <summary>
/// The input half of Audio Toolbox's Audio Queue Services: a queue that records into
/// buffers it hands back one at a time, converting the device's rate and channels to the
/// format it was made with.
/// </summary>
/// <remarks>
/// Resolved lazily, like <see cref="AudioToolbox"/>, so the assembly still loads where
/// there is no Audio Toolbox.
/// </remarks>
internal static unsafe partial class AudioQueue
{
    /// <summary>Audio Queue Services live in Audio Toolbox.</summary>
    private const string Library = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    /// <summary><c>kAudioQueueProperty_CurrentDevice</c> — the UID of the device a queue listens to, as a <c>CFStringRef</c>.</summary>
    public const uint CurrentDeviceProperty = 0x6171_6364; // 'aqcd'

    /// <summary>C's <c>AudioQueueBuffer</c>; the queue owns it and its data.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Buffer
    {
        public uint AudioDataBytesCapacity;
        public IntPtr AudioData;
        public uint AudioDataByteSize;
        public IntPtr UserData;
        public uint PacketDescriptionCapacity;
        public IntPtr PacketDescriptions;
        public uint PacketDescriptionCount;
    }

    /// <summary>
    /// Makes an input queue. A null run loop has the queue call back on a thread of its own.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "AudioQueueNewInput")]
    public static partial int NewInput(
        in AudioToolbox.AudioStreamBasicDescription format,
        delegate* unmanaged[Cdecl]<IntPtr, IntPtr, Buffer*, IntPtr, uint, IntPtr, void> callback,
        IntPtr context,
        IntPtr runLoop,
        IntPtr runLoopMode,
        uint flags,
        out IntPtr queue);

    [LibraryImport(Library, EntryPoint = "AudioQueueAllocateBuffer")]
    public static partial int AllocateBuffer(IntPtr queue, uint bytes, out Buffer* buffer);

    [LibraryImport(Library, EntryPoint = "AudioQueueEnqueueBuffer")]
    public static partial int EnqueueBuffer(IntPtr queue, Buffer* buffer, uint packetDescriptions, IntPtr descriptions);

    [LibraryImport(Library, EntryPoint = "AudioQueueSetProperty")]
    public static partial int SetProperty(IntPtr queue, uint id, in IntPtr data, uint size);

    [LibraryImport(Library, EntryPoint = "AudioQueueStart")]
    public static partial int Start(IntPtr queue, IntPtr startTime);

    /// <summary>With <paramref name="immediate"/> set, returns only once no callback is running or will run.</summary>
    [LibraryImport(Library, EntryPoint = "AudioQueueStop")]
    public static partial int Stop(IntPtr queue, [MarshalAs(UnmanagedType.U1)] bool immediate);

    /// <summary>Frees the queue and every buffer it allocated.</summary>
    [LibraryImport(Library, EntryPoint = "AudioQueueDispose")]
    public static partial int Dispose(IntPtr queue, [MarshalAs(UnmanagedType.U1)] bool immediate);
}
