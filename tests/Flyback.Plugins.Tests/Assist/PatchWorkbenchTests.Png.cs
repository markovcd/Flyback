using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Render;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Assist;

public partial class PatchWorkbenchTests
{
    // --- reading a png without decoding one ---------------------------------

    private static string Signature(byte[] png) => System.Text.Encoding.ASCII.GetString(png, 1, 3);

    private static int Width(byte[] png) => BigEndian(png, 16);

    private static int Height(byte[] png) => BigEndian(png, 20);

    private static int BigEndian(byte[] bytes, int at) =>
        (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];

    // A RIFF header, read at the offsets WavWriter writes it at. Little-endian,
    // where PNG is big — which is most of why these are two sets of helpers.
    private static string Riff(byte[] wav) => System.Text.Encoding.ASCII.GetString(wav, 0, 4);

    private static string Format(byte[] wav) => System.Text.Encoding.ASCII.GetString(wav, 8, 4);

    private static int Channels(byte[] wav) => LittleEndian(wav, 22, 2);

    private static int Rate(byte[] wav) => LittleEndian(wav, 24, 4);

    private static int DataBytes(byte[] wav) => LittleEndian(wav, 40, 4);

    private static int LittleEndian(byte[] bytes, int at, int width)
    {
        var value = 0;
        for (var i = width - 1; i >= 0; i--) value = (value << 8) | bytes[at + i];
        return value;
    }
}
