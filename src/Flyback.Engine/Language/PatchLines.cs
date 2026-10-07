using Flyback.Core.Graph;
using Flyback.Engine.Language.Ast.Statements;

namespace Flyback.Engine.Language;

/// <summary>
/// The lines that speak of the whole patch, each said once: its keyboard, its
/// length, its description, its author and its tags.
/// </summary>
internal sealed class PatchLines(Patch patch, Issues issues)
{
    /// <summary>Whether a <c>keyboard</c> line has been read already, so a second is said rather than obeyed.</summary>
    private bool laid;

    /// <summary>Lays the computer keyboard out, once — there is one keyboard.</summary>
    public void Lay(KeyboardStatement statement)
    {
        if (laid)
        {
            issues.Complain(IssueCode.SaidTwice, statement.Line, statement.Column,
                "the keyboard is already laid out further up. A patch has one keyboard, so it says so once.");
            return;
        }

        laid = true;
        patch.Keyboard = statement.Scale is { } block ? Scale(block, statement.BlockLine, statement.BlockColumn) : null;
    }

    /// <summary>
    /// The notes of a scale from its tonic, <c>[ D E F G A B C ]</c>, which have to
    /// be one of the scales an Auto Chord builds in.
    /// </summary>
    private KeyboardScale? Scale(string block, int line, int column)
    {
        var said = issues.Count;
        var notes = StepNotation.Classes(block, line, column, issues.List);

        // A word that is not a note has been said already, and the scale it spoils is not worth saying too.
        if (issues.Count > said) return null;

        if (notes.Count == 0)
        {
            issues.Complain(IssueCode.UnknownScale, line, column,
                "the keyboard's scale has no notes. Spell one from its tonic, as in 'keyboard scale [ D E F G A B C ]'.");
            return null;
        }

        var tonic = notes[0];
        var classes = Pitch.Scale(notes.Select(note => (note - tonic + Pitch.Classes) % Pitch.Classes));

        if (Chords.Scales.FirstOrDefault(scale => scale.Classes.SequenceEqual(classes)) is { } mode)
            return new KeyboardScale(tonic, mode.Id);

        issues.Complain(IssueCode.UnknownScale, line, column,
            $"{string.Join(" ", notes.Select(Pitch.ClassName))} is not a scale from {Pitch.ClassName(tonic)}. "
            + "The keyboard plays the seven-note scales an Auto Chord builds in, from the first note.");
        return null;
    }

    /// <summary>Says how long the patch plays for, once.</summary>
    public void Last(LengthStatement statement)
    {
        if (patch.Length is not null)
        {
            issues.Complain(IssueCode.SaidTwice, statement.Line, statement.Column,
                "the patch's length is already said further up. It has one length, so it says so once.");
            return;
        }

        patch.Length = statement.Seconds;
    }

    /// <summary>Says what the patch is for, once.</summary>
    public void Describe(DescriptionStatement statement)
    {
        if (patch.Description is not null)
        {
            issues.Complain(IssueCode.SaidTwice, statement.Line, statement.Column,
                "the patch is already described further up. It has one description, so it says so once.");
            return;
        }

        patch.Describe(statement.Text);
    }

    /// <summary>Says who made the patch, once.</summary>
    public void Credit(AuthorStatement statement)
    {
        if (patch.Author is not null)
        {
            issues.Complain(IssueCode.SaidTwice, statement.Line, statement.Column,
                "the patch is already credited further up. It has one author line, so it says so once.");
            return;
        }

        patch.Credit(statement.Text);
    }

    /// <summary>Tags the patch, once.</summary>
    public void Tag(TagsStatement statement)
    {
        if (patch.Tags is not null)
        {
            issues.Complain(IssueCode.SaidTwice, statement.Line, statement.Column,
                "the patch is already tagged further up. It has one tags line, so it says so once.");
            return;
        }

        patch.Tag(statement.Tags);
    }
}
