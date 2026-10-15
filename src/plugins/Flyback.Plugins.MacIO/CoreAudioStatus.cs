using System.Globalization;
using System.Text;

namespace Flyback.Plugins.MacIO;

/// <summary>What an <c>OSStatus</c> from CoreAudio or Audio Toolbox reads as in a message.</summary>
internal static class CoreAudioStatus
{
    /// <summary>
    /// CoreAudio reports most failures as a four-character code arriving as a
    /// signed integer — <c>'fmt?'</c> is far easier to look up than -10868.
    /// </summary>
    public static string Describe(int status)
    {
        Span<byte> code = [(byte)(status >> 24), (byte)(status >> 16), (byte)(status >> 8), (byte)status];

        foreach (var b in code)
            if (b is < 0x20 or > 0x7E)
                return status.ToString(CultureInfo.InvariantCulture);

        return $"'{Encoding.ASCII.GetString(code)}' ({status.ToString(CultureInfo.InvariantCulture)})";
    }
}
