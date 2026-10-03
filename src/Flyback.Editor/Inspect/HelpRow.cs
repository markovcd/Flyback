namespace Flyback.Editor.Inspect;

/// <summary>
/// One thing the canvas can do, as the shortcut list shows it.
/// </summary>
/// <param name="Keys">
/// What is pressed, as it is drawn: "A / B" are alternatives and "A+B" are held
/// together. Empty where nothing is pressed.
/// </param>
internal sealed record HelpRow(string Title, string Detail, string Keys);
