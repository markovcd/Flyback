namespace Flyback.Plugins.Surveys;

/// <summary>
/// What one question came back as. Three rather than two because a limit and a
/// refusal look the same to a caller that only asks whether it worked.
/// </summary>
internal enum ProbeVerdict
{
    Took,
    Refused,
    Unclear,
}
