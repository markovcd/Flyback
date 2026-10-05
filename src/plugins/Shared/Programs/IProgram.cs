namespace Flyback.Plugins.Programs;

/// <summary>Asks the program one question and waits for its answer.</summary>
internal interface IProgram
{
    /// <exception cref="ProgramFailure">It was not installed, not signed in, or said no.</exception>
    Task<ProgramAnswer> Ask(ProgramQuestion question, CancellationToken cancel);
}
