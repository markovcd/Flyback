using System.CommandLine.Parsing;
using Flyback.Engine.Render;

namespace Flyback.Cli.Common;

/// <summary>A <c>--size</c> as WIDTHxHEIGHT, and a complaint in the shell's own words when it is not.</summary>
internal static class SizeArgument
{
    public static (int Width, int Height) Parse(ArgumentResult result)
    {
        var text = result.Tokens[0].Value;

        if (FrameSize.Of(text) is { } size) return size;

        result.AddError(FrameSize.Refuse(text));
        return (0, 0);
    }
}
