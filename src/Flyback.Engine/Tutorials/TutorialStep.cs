namespace Flyback.Engine.Tutorials;

/// <summary>One thing to do, and the patch that shows it was done.</summary>
/// <param name="Title">What to do, as a heading.</param>
/// <param name="Text">How to do it.</param>
/// <param name="Goal">
/// Text-language source for what the patch must hold once the step is done, or null for a step
/// that is read and moved past by hand.
/// </param>
internal sealed record TutorialStep(string Title, string Text, string? Goal);
