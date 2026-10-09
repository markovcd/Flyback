namespace Flyback.Plugins.Surveys;

/// <summary>One question's verdict, and what the endpoint said with it.</summary>
/// <param name="Detail">What the endpoint said went wrong, or empty.</param>
internal sealed record ProbeAnswer(ProbeVerdict Verdict, string Detail);
