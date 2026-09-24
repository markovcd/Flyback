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

    // --- reading the statements ------------------------------------------------

    /// <summary>Something the grammar needed and did not find, which the message names.</summary>
    public const string Syntax = "syntax";

    public const string UnreadTail = "unread-tail";
    public const string EmptyBody = "empty-body";
    public const string UnknownLayout = "unknown-layout";
    public const string ArithmeticAfterPipeline = "arithmetic-after-pipeline";

    // --- step blocks -----------------------------------------------------------

    public const string StepSyntax = "step-syntax";
    public const string UnknownNote = "unknown-note";
    public const string EuclidNeedsLength = "euclid-needs-length";

    // --- names -----------------------------------------------------------------

    public const string UnknownName = "unknown-name";

    /// <summary>A module name nothing has; the fix is the nearest one, where one is close.</summary>
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

    /// <summary>A pipe into a module with no socket it lands on unsaid. No fix: which socket is the writer's to say.</summary>
    public const string PipeLandsNowhere = "pipe-lands-nowhere";

    public const string NoSocketFree = "no-socket-free";
    public const string PipelineInArgument = "pipeline-in-argument";
    public const string BadStage = "bad-stage";
    public const string PlaceholderMisplaced = "placeholder-misplaced";
    public const string PlaceholderTwice = "placeholder-twice";

    // --- values ----------------------------------------------------------------

    public const string WrongLiteral = "wrong-literal";
    public const string ScaledArithmetic = "scaled-arithmetic";

    /// <summary>A bare number on a length of time, which is a power of ten. No fix: seconds or decades is the writer's to say.</summary>
    public const string BareDuration = "bare-duration";

    public const string OutOfRange = "out-of-range";
    public const string NumberTooLarge = "number-too-large";

    // --- what a module carries -------------------------------------------------

    public const string NoFile = "no-file";
    public const string NoBlock = "no-block";

    // --- defs, tuples and the patch as a whole ---------------------------------

    public const string TupleMismatch = "tuple-mismatch";
    public const string DefCallsItself = "def-calls-itself";
    public const string DefArity = "def-arity";
    public const string OutputCannotBeOff = "output-cannot-be-off";

    // --- the panel -------------------------------------------------------------

    public const string PanelInDef = "panel-in-def";
    public const string PanelNotASignal = "panel-not-a-signal";
    public const string UnknownSetting = "unknown-setting";

    /// <summary>A line a patch has once — keyboard, description, author, tags — said again.</summary>
    public const string SaidTwice = "said-twice";
}
