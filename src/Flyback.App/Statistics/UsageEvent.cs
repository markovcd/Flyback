namespace Flyback.App.Statistics;

/// <summary>
/// One thing a run has to say about itself: a name, and numbers and names beside
/// it. Everything that may appear in one is listed in ADR-0094 and ADR-0103.
/// </summary>
/// <param name="Name">What happened — <c>started</c>, <c>played</c>, <c>assistant</c>, <c>ended</c>, <c>crashed</c>.</param>
/// <param name="Props">
/// What it carries. A value is a string, a number or a flag; nothing else survives
/// the far end, which sorts them into words and numbers.
/// </param>
internal sealed record UsageEvent(string Name, IReadOnlyDictionary<string, object> Props);