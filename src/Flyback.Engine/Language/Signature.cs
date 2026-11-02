namespace Flyback.Engine.Language;

/// <summary>The call the caret is in: what it calls, every socket it takes, which one the caret is filling and which the pipe fills.</summary>
/// <param name="Callee">As the text writes it.</param>
/// <param name="Title">What it calls, said for a person: <c>Sine (osc.sine)</c>, or <c>def voice</c>.</param>
/// <param name="Active">The socket the argument at the caret goes to, or -1 where none is left for it.</param>
/// <param name="Piped">The socket a pipe into the call lands on, or -1.</param>
internal sealed record Signature(string Callee, string Title, IReadOnlyList<SignatureSocket> Sockets, int Active, int Piped);
