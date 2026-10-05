namespace Flyback.Plugins.Programs;

/// <summary>The program could not answer; the message is what the person is told.</summary>
internal sealed class ProgramFailure(string message) : Exception(message);
