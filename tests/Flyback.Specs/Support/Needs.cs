using Flyback.Tests;
using Reqnroll.UnitTestProvider;

namespace Flyback.Specs.Support;

/// <summary>
/// What a scenario needs of the machine: skipped where it is missing, and a failure in
/// the gate, whose image carries every tool a scenario needs.
/// </summary>
internal static class Needs
{
    /// <summary>Set by the gate's test run.</summary>
    private static readonly bool InGate = Environment.GetEnvironmentVariable("FLYBACK_GATE") == "1";

    /// <summary>Skips the scenario, saying <paramref name="missing"/>, unless <paramref name="here"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// The scenario is not tagged with <paramref name="category"/>, or it is the gate, where a missing tool is a regression in its image.
    /// </exception>
    public static void Tool(IUnitTestRuntimeProvider runtime, TestCategory category, bool here, string missing)
    {
        category.Carried();

        if (here) return;

        if (InGate) throw new InvalidOperationException($"{missing}, and the gate's image is built with it");

        runtime.TestIgnore(missing);
    }
}
