using Avalonia.Controls;
using Avalonia.Input;

namespace Flyback.App.Controls;

/// <summary>
/// A list you point at. A <see cref="ComboBox"/> in every way but one: no keystroke
/// changes what it says.
/// </summary>
/// <remarks>
/// A ComboBox answers the keyboard twice over — an arrow moves the selection with
/// the dropdown shut, a letter jumps to the first matching item — and both commit,
/// raising SelectionChanged for every step. Picking a preset throws away the patch
/// on the canvas, so arrowing through fourteen throws it away fourteen times; and
/// a bare letter belongs to the instrument, so a picker that took one would answer
/// a note by changing the patch under it.
/// <para>
/// Ignored rather than marked handled, so the event goes on to the window exactly
/// as though the picker were not focused: the letters still play and the shortcuts
/// still work. Tab is untouched, being the TopLevel's business.
/// </para>
/// </remarks>
internal sealed class Picker : ComboBox
{
    /// <summary>
    /// Borrows the ComboBox's own look, because a control gets none of its own.
    /// </summary>
    /// <remarks>
    /// A theme is found by type, and the type looked for is this one unless it says
    /// otherwise — so without this line a Picker matches no <c>ControlTheme</c>, is
    /// given no template, and draws nothing while still answering every question a
    /// test asks it.
    /// </remarks>
    protected override Type StyleKeyOverride => typeof(ComboBox);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Escape closes a dropdown that is open, and that one is kept: a list
        // opened by a misclick needs a way out that is not another click, and
        // closing one changes nothing about what it says.
        if (e.Key == Key.Escape && IsDropDownOpen) base.OnKeyDown(e);
    }

    /// <summary>
    /// Where the jump-to-a-letter comes from. Not passed on, so a letter typed at
    /// a focused picker is a letter typed at nothing.
    /// </summary>
    protected override void OnTextInput(TextInputEventArgs e)
    {
    }
}
