using System.Runtime.InteropServices;

namespace Flyback.Plugins.Ndi;

/// <summary>NDI's <c>NDIlib_video_frame_v2_t</c>, laid out as the C header has it.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NdiVideoFrame
{
    /// <summary>
    /// <c>NDIlib_FourCC_type_BGRX</c>: four bytes a pixel, blue first, the fourth ignored.
    /// The picture is opaque, and a receiver honoring alpha would see through it wherever a shader left alpha low.
    /// </summary>
    public const int Bgrx = 'B' | ('G' << 8) | ('R' << 16) | ('X' << 24);

    /// <summary><c>NDIlib_frame_format_type_progressive</c>.</summary>
    public const int Progressive = 1;

    public int Width;
    public int Height;
    public int FourCC;
    public int FrameRateNumerator;
    public int FrameRateDenominator;
    public float AspectRatio;
    public int FrameFormat;

    /// <summary>When the frame is of, in 100-nanosecond units, which lines it up with sound sent beside it.</summary>
    public long Timecode;

    public IntPtr Data;
    public int LineStride;
    /// <summary>No metadata is sent; the field holds the C layout, which a test pins.</summary>
    public IntPtr Metadata;

    /// <summary>Filled in by NDI on receipt; the field holds the C layout, which a test pins.</summary>
    public long Timestamp;
}
