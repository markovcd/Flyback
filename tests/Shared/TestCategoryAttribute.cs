using Xunit.v3;

namespace Flyback.Tests;

/// <summary>Files a test class or method under a <see cref="TestCategory"/>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
internal sealed class TestCategoryAttribute(TestCategory category) : Attribute, ITraitAttribute
{
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() => [new(TestCategories.Trait, TestCategories.Name(category))];
}
