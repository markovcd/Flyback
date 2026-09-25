namespace Flyback.Core.Language;

/// <summary>
/// What kind of mistake a <see cref="LanguageIssue"/> is, as a word a program
/// can match on. The message is for people and may be reworded; a code is not.
/// </summary>
public static class IssueCode
{
    // --- reading the characters ------------------------------------------------

    public const string UnclosedText = "unclosed-text";
    public const string UnclosedBlock = "unclosed-block";
    public const string StrayCharacter = "stray-character";

    /// <summary>A comment written with <c>//</c>, which the language writes with <c>#</c>.</summary>
    public const string SlashComment = "slash-comment";

    /// <summary>Digits run on into letters or a second point: <c>1e3</c>, <c>0x10</c>, <c>220Hz</c>, <c>1.5.5</c>.</summary>
    public const string MalformedNumber = "malformed-number";

    /// <summary>A typographic character standing in for the plain one the language reads: a curly quote, a long minus.</summary>
    public const string LookalikeCharacter = "lookalike-character";

    /// <summary>An arrow or a bar written where a pipe, <c>|&gt;</c>, was meant.</summary>
    public const string NotAPipe = "not-a-pipe";

    /// <summary>A <c>;</c>, which the language has no use for: a statement ends with its line.</summary>
    public const string Semicolon = "semicolon";

    /// <summary>A <c>)</c>, <c>]</c> or <c>}</c> with nothing open for it to close.</summary>
    public const string UnmatchedCloser = "unmatched-closer";

    /// <summary>Brackets, minus signs or arithmetic nested deeper than the text is read to.</summary>
    public const string TooDeep = "too-deep";

    // --- reading the statements ------------------------------------------------

    /// <summary>Something the grammar needed and did not find, which the message names.</summary>
    public const string Syntax = "syntax";

    public const string UnreadTail = "unread-tail";
    public const string EmptyBody = "empty-body";
    public const string UnknownLayout = "unknown-layout";

    /// <summary>A keyboard laid out in notes that are not one of the scales an Auto Chord builds in.</summary>
    public const string UnknownScale = "unknown-scale";
    /// <summary>A <c>length</c> that is not minutes:seconds or a duration, or is shorter than a tenth of a second or longer than a day.</summary>
    public const string BadLength = "bad-length";
    public const string ArithmeticAfterPipeline = "arithmetic-after-pipeline";

    // --- step blocks -----------------------------------------------------------

    public const string StepSyntax = "step-syntax";
    public const string UnknownNote = "unknown-note";
    public const string EuclidNeedsLength = "euclid-needs-length";

    /// <summary>A block that spells more steps than a sequence holds.</summary>
    public const string TooManySteps = "too-many-steps";

    // --- names -----------------------------------------------------------------

    public const string UnknownName = "unknown-name";

    /// <summary>A name read above the line that binds it.</summary>
    public const string UsedBeforeBound = "used-before-bound";
    public const string UnknownModule = "unknown-module";

    public const string AmbiguousModule = "ambiguous-module";
    public const string ReservedName = "reserved-name";
    public const string BoundTwice = "bound-twice";
    public const string DefTwice = "def-twice";
    public const string NotAModule = "not-a-module";
    public const string OutputIsNotASource = "output-is-not-a-source";

    // --- calls and sockets -----------------------------------------------------

    public const string UnknownSocket = "unknown-socket";
    public const string UnknownOutput = "unknown-output";
    public const string SocketUnsaid = "socket-unsaid";
    public const string GivenTwice = "given-twice";
    public const string TooManyArguments = "too-many-arguments";
    public const string WiredTwice = "wired-twice";
    public const string KnobSetTwice = "knob-set-twice";
    public const string NormalledSocket = "normalled-socket";
    public const string NotASignal = "not-a-signal";
    public const string KnobNeedsNumber = "knob-needs-number";
    public const string FieldNeedsValue = "field-needs-value";
    public const string RangeOutsideArgument = "range-outside-argument";

    // --- pipes -----------------------------------------------------------------

    /// <summary>A pipe into a module with no socket it lands on unsaid.</summary>
    public const string PipeLandsNowhere = "pipe-lands-nowhere";

    public const string NoSocketFree = "no-socket-free";
    public const string PipelineInArgument = "pipeline-in-argument";
    public const string BadStage = "bad-stage";
    public const string PlaceholderMisplaced = "placeholder-misplaced";
    public const string PlaceholderTwice = "placeholder-twice";

    // --- values ----------------------------------------------------------------

    public const string WrongLiteral = "wrong-literal";
    public const string ScaledArithmetic = "scaled-arithmetic";

    /// <summary>A bare number on a length of time, which is a power of ten.</summary>
    public const string BareDuration = "bare-duration";

    public const string OutOfRange = "out-of-range";
    public const string NumberTooLarge = "number-too-large";

    // --- what a module carries -------------------------------------------------

    /// <summary>A plugin a <c>requires</c> line names that this build does not have. Said once, for every module it would have given.</summary>
    public const string MissingPlugin = "missing-plugin";

    public const string NoFile = "no-file";
    public const string NoBlock = "no-block";

    // --- defs, tuples and the patch as a whole ---------------------------------

    public const string TupleMismatch = "tuple-mismatch";
    public const string DefCallsItself = "def-calls-itself";
    public const string DefArity = "def-arity";
    public const string OutputCannotBeOff = "output-cannot-be-off";

    // --- the panel -------------------------------------------------------------

    public const string PanelInDef = "panel-in-def";
    public const string PanelInGroup = "panel-in-group";

    // --- groups ----------------------------------------------------------------

    public const string RequiresInGroup = "requires-in-group";
    public const string GroupInGroup = "group-in-group";
    public const string GroupTooSmall = "group-too-small";
    public const string PanelNotASignal = "panel-not-a-signal";
    public const string UnknownSetting = "unknown-setting";

    /// <summary>A line a patch has once — keyboard, length, description, author, tags — said again.</summary>
    public const string SaidTwice = "said-twice";
}
