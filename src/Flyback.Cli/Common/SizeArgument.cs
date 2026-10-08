using System.CommandLine.Parsing;
using Flyback.Engine.Render;
using Flyback.Host;

namespace Flyback.Cli.Common;

/// <summary>A <c>--size</c> as WIDTHxHEIGHT or a name the settings offer, and a complaint in the shell's own words when it is neither.</summary>
internal static class SizeArgument
{
    public static (int Width, int Height) Parse(ArgumentResult result)
    {
        var text = result.Tokens[0].Value;

        if (Resolutions.Named(text) is { } named) return named;

        if (FrameSize.Of(text) is { } size) return size;

        result.AddError(FrameSize.Refuse(text));
        return (0, 0);
    }
}
