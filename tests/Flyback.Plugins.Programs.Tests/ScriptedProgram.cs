namespace Flyback.Plugins.Programs.Tests;

/// <summary>A program replaced by replies written out in advance, remembering what it was asked.</summary>
internal sealed class ScriptedProgram(params string[] replies) : IProgram
{
    private readonly Queue<string> left = new(replies);

    /// <summary>Each question, as asked.</summary>
    public List<ProgramQuestion> Asked { get; } = [];

    /// <summary>The last question's turns, as text, with the preamble ahead of them.</summary>
    public string LastText => Text(Asked[^1]);

    /// <summary>The preamble and every turn of <paramref name="question"/> as one string.</summary>
    public static string Text(ProgramQuestion question) =>
        question.Preamble + "\n" + string.Join("\n", question.Turns.Select(t => $"<{t.Role}>\n{t.Text}\n</{t.Role}>"));

    public Task<ProgramAnswer> Ask(ProgramQuestion question, CancellationToken cancel)
    {
        Asked.Add(question);

        return left.TryDequeue(out var reply)
            ? Task.FromResult(new ProgramAnswer(reply, 100, 50, 10))
            : throw new ProgramFailure("nothing left to say.");
    }
}
