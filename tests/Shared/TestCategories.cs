using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Flyback.Tests;

/// <summary>Holds a test that needs a <see cref="TestCategory"/> to being filed under it.</summary>
internal static class TestCategories
{
    /// <summary>The trait a category is carried as, the one a feature's tags become.</summary>
    public const string Trait = "Category";

    /// <summary>The trait's value and the tag's name: <c>ffmpeg</c> for <see cref="TestCategory.Ffmpeg"/>.</summary>
    public static string Name(TestCategory category) => category.ToString().ToLowerInvariant();

    /// <summary>Skips the test, saying <paramref name="missing"/>, unless <paramref name="here"/>.</summary>
    /// <exception cref="InvalidOperationException">The test is not filed under <paramref name="category"/>.</exception>
    public static void Require(this TestCategory category, [DoesNotReturnIf(false)] bool here, string missing)
    {
        category.Carried();
        Assert.SkipUnless(here, missing);
    }

    /// <summary>Fails a test that needs <paramref name="category"/> and is not filed under it, whether or not the machine has it.</summary>
    /// <exception cref="InvalidOperationException">The running test does not carry the category.</exception>
    public static void Carried(this TestCategory category)
    {
        var test = TestContext.Current.Test;
        var name = Name(category);

        if (test is not null && test.Traits.TryGetValue(Trait, out var values) && values.Contains(name)) return;

        throw new InvalidOperationException(
            $"{test?.TestDisplayName ?? "This test"} needs {name} and is not filed under it: mark it [TestCategory(TestCategory.{category})], or tag its scenario @{name}.");
    }
}
