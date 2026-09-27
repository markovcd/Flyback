namespace Flyback.Core.Graph.Extras;

/// <summary>
/// Settings a module carries that are not sockets — an order, a mode, a count —
/// for a module with nothing more to say about them than what they are.
/// </summary>
/// <remarks>
/// Everything <see cref="NodeExtra"/> does is already written in terms of
/// <see cref="NodeExtra.Fields"/>, so a kind whose whole content is its fields
/// is a key and a list. An emit function reads what was chosen back with
/// <see cref="EmitContext.Extra{T}"/> as an <see cref="ExtraState"/>, under the
/// same key. A setting rather than a socket wherever the choice decides which ops
/// are emitted rather than what they compute, which a signal cannot do.
/// </remarks>
/// <param name="StateKey">What the settings are filed under — see <see cref="NodeExtra.Key"/>.</param>
/// <param name="Declared">The settings, in the order the inspector lists them.</param>
public sealed record SettingsExtra(string StateKey, IReadOnlyList<ExtraField> Declared) : NodeExtra
{
    public override string Key => StateKey;

    public override IReadOnlyList<ExtraField> Fields => Declared;
}