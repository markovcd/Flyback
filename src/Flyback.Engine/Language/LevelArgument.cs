namespace Flyback.Engine.Language;

/// <summary>One argument a def call was given, as the text would write it.</summary>
/// <param name="Parameter">The def's name for it.</param>
/// <param name="Value">What it was given: <c>55</c>, <c>A2</c>, <c>osc.out</c>.</param>
public sealed record LevelArgument(string Parameter, string Value)
{
    public override string ToString() => $"{Parameter} = {Value}";
}
