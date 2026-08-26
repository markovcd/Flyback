using Avalonia.Controls;
using Avalonia.Input;

namespace Flyback.App.Controls;

/// <summary>
/// A list you point at. It is a <see cref="ComboBox"/> in every way but one: no
/// keystroke changes what it says.
/// </summary>
/// <remarks>
/// A ComboBox answers the keyboard twice over. An arrow moves the selection with
/// the dropdown shut, and a letter jumps to the first item beginning with it —
/// and both of those *commit*, raising SelectionChanged for every step on the
/// way. That is the ordinary behaviour of a list of harmless options, and it is
/// the wrong behaviour for every list in this window.
/// <para>
/// Two things make it wrong here. What these lists do is not harmless: picking a
/// preset throws away the patch on the canvas, and arrowing through fourteen of
/// them throws it away fourteen times. And a bare letter belongs to the
/// instrument now — a patch holding a MIDI In is played on the letters, so a
/// picker that took one would answer a note by changing the patch under it.
/// </para>
/// <para>
/// Ignored rather than marked handled, which is the whole of the difference
/// between this and swallowing the key. The event goes on to the window exactly
/// as though the picker were not focused, so the letters still play and the
/// shortcuts still work; what is given up is only the picker's own reading of
/// them. Tab is untouched for the same reason — moving the focus is the
/// TopLevel's business and was never this control's.
/// </para>
/// </remarks>
internal sealed class Picker : ComboBox
{
    /// <summary>
    /// Borrows the ComboBox's own look, because a control gets none of its own.
    /// </summary>
    /// <remarks>
    /// A theme is found by type, and the type looked for is this one unless it
    /// says otherwise — so without this line a Picker matches no
    /// <c>ControlTheme</c> at all, is given no template, and draws nothing.
    /// Everything still works: it holds its items, it raises SelectionChanged,
    /// and every test that asked it a question got the right answer. It is simply
    /// invisible, which is the one thing a test that never looks cannot see.
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
