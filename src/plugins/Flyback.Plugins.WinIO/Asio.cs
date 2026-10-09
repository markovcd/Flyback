using System.Runtime.InteropServices;

namespace Flyback.Plugins.WinIO;

/// <summary>
/// The numbers and structs of Steinberg's ASIO interface this plugin needs, hand-written like
/// <see cref="WinMm"/>. ASIO's <c>long</c> is 32 bits on Windows.
/// </summary>
internal static unsafe class Asio
{
    /// <summary><c>ASE_OK</c>.</summary>
    public const int Ok = 0;

    /// <summary><c>ASE_SUCCESS</c>: what <c>future</c> answers for a selector it carried out.</summary>
    public const int Success = 0x3f4847a0;

    // ASIOSampleType: little-endian only, since no Windows machine is anything else.
    public const int Int16 = 16;      // ASIOSTInt16LSB
    public const int Int24 = 17;      // ASIOSTInt24LSB, packed in three bytes
    public const int Int32 = 18;      // ASIOSTInt32LSB
    public const int Float32 = 19;    // ASIOSTFloat32LSB
    public const int Float64 = 20;    // ASIOSTFloat64LSB
    public const int Int32In16 = 24;  // ASIOSTInt32LSB16: 16 bits right-aligned in 32
    public const int Int32In18 = 25;  // ASIOSTInt32LSB18
    public const int Int32In20 = 26;  // ASIOSTInt32LSB20
    public const int Int32In24 = 27;  // ASIOSTInt32LSB24

    // asioMessage selectors.
    public const int SelectorSupported = 1;  // kAsioSelectorSupported
    public const int EngineVersion = 2;      // kAsioEngineVersion
    public const int ResetRequest = 3;       // kAsioResetRequest
    public const int ResyncRequest = 5;      // kAsioResyncRequest
    public const int LatenciesChanged = 6;   // kAsioLatenciesChanged

    /// <summary>The bytes one sample of <paramref name="type"/> takes, or 0 for a type this plugin cannot write.</summary>
    public static int Width(int type) => type switch
    {
        Int16 => 2,
        Int24 => 3,
        Int32 or Float32 or Int32In16 or Int32In18 or Int32In20 or Int32In24 => 4,
        Float64 => 8,
        _ => 0,
    };

    /// <summary><c>ASIOChannelInfo</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ChannelInfo
    {
        public int Channel;
        public int IsInput;
        public int IsActive;
        public int ChannelGroup;
        public int Type;
        public fixed byte Name[32];
    }

    /// <summary><c>ASIOBufferInfo</c>: the driver fills in the two halves of the channel's double buffer.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BufferInfo
    {
        public int IsInput;
        public int Channel;
        public IntPtr First;
        public IntPtr Second;
    }

    /// <summary><c>ASIOCallbacks</c>. None carries a pointer back to the host, so only one driver plays at a time.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Callbacks
    {
        public delegate* unmanaged[Cdecl]<int, int, void> BufferSwitch;
        public delegate* unmanaged[Cdecl]<double, void> SampleRateDidChange;
        public delegate* unmanaged[Cdecl]<int, int, IntPtr, double*, int> Message;
        public delegate* unmanaged[Cdecl]<IntPtr, int, int, IntPtr> BufferSwitchTimeInfo;
    }
}
