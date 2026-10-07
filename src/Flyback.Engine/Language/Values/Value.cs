using Flyback.Core.Graph;

namespace Flyback.Engine.Language.Values;

/// <summary>Anything a name or an expression can stand for while binding.</summary>
internal abstract record Value
{
    /// <summary>How many signals this value carries when it is piped.</summary>
    public int Width => this switch
    {
        Placed placed => placed.Def.Outputs.Count,
        Several several => several.Items.Count,
        _ => 1,
    };

    /// <summary>The <paramref name="index"/>th signal of this value, for wiring.</summary>
    public Value Part(int index) => this switch
    {
        Placed placed => new Socket(placed.Id, placed.Def, index),
        Several several => several.Items[index],
        _ => this,
    };

    /// <summary>
    /// What the first signal is declared as, or null where nothing declares it:
    /// a number, or a Maths module passing on whatever it reads.
    /// </summary>
    public PortKind? Kind => this switch
    {
        Placed placed => placed.Def.Outputs.Count > 0 ? placed.Def.Outputs[0].Kind : null,
        Socket socket => socket.Port < socket.Def.Outputs.Count ? socket.Def.Outputs[socket.Port].Kind : null,
        Several several when several.Items.Count > 0 => several.Items[0].Kind,
        _ => null,
    };
}
