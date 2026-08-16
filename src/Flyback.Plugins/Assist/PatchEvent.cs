using Flyback.Core.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>
/// One thing that happened while an assistant was working.
/// </summary>
/// <remarks>
/// A closed set, because the shell has to be able to show a run without knowing
/// whose provider produced it. Anything a particular provider does that does not
/// fit one of these is that provider's business and does not reach the window.
/// </remarks>
public abstract record PatchEvent
{
    private PatchEvent()
    {
    }

    /// <summary>A fragment of prose as it arrives. Append it; do not replace what is there.</summary>
    public sealed record Said(string Text) : PatchEvent;

    /// <summary>An edit that has already been made to the working patch.</summary>
    public sealed record Did(string Summary) : PatchEvent;

    /// <summary>A frame it drew and looked at, as PNG bytes.</summary>
    public sealed record Saw(byte[] Png, string Caption) : PatchEvent;

    /// <summary>
    /// The finished patch, laid out and copied. The turn is over; nothing has
    /// reached the editor.
    /// </summary>
    public sealed record Proposed(Patch Patch, string Summary) : PatchEvent;

    /// <summary>
    /// What the turn cost, as the provider reports it. <paramref name="CacheRead"/>
    /// is worth showing on its own: the module handbook is a large stable prefix,
    /// and a cache that has quietly stopped being hit is expensive rather than
    /// broken, so nothing else would surface it.
    /// </summary>
    public sealed record Cost(int Input, int CacheRead, int Output) : PatchEvent;

    /// <summary>
    /// It could not finish. A value rather than an exception, for the reason
    /// <see cref="Hosting.PluginProblem"/> is one: nothing a plugin does should
    /// be able to take the shell down with it.
    /// </summary>
    public sealed record Failed(string Message) : PatchEvent;
}
