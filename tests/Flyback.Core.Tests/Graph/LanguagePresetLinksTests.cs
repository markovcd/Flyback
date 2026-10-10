using System.Reflection;
using System.Text.RegularExpressions;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Tests;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// Each preset transliterated in <c>docs/language.md</c> links to the method that builds it,
/// so a renamed preset method fails here rather than leaving the doc pointing at nothing.
/// </summary>
public sealed partial class LanguagePresetLinksTests
{
    [Fact]
    public void Each_transliterated_preset_names_a_method_that_builds_it()
    {
        var doc = File.ReadAllText(Repository.Path("docs", "language.md"));
        var named = PresetLink().Matches(doc).Select(m => m.Groups[1].Value).ToList();

        named.ShouldNotBeEmpty("docs/language.md should link its transliterated presets to Presets by method name");

        var built = typeof(Presets).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(Patch))
            .Select(m => m.Name)
            .ToHashSet();

        named.Where(name => !built.Contains(name)).ShouldBeEmpty("docs/language.md links these, and Presets has no such method");
    }

    [GeneratedRegex(@"^### .+ — \[`Presets\.(\w+)`\]", RegexOptions.Multiline)]
    private static partial Regex PresetLink();
}
