using System.Reflection;
using Avalonia.Headless.XUnit;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// What every test on the UI fixture has to be, checked rather than written down.
/// Not a <see cref="UiTest"/> itself, or it would break its own rule.
/// </summary>
public sealed class UiFixtureTests
{
    [Fact]
    public void No_test_on_the_ui_fixture_carries_a_plain_fact()
    {
        var stray =
            from type in typeof(UiTest).Assembly.GetTypes()
            where type != typeof(UiTest) && typeof(UiTest).IsAssignableFrom(type)
            from method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            where method.GetCustomAttribute<FactAttribute>() is not null
                && method.GetCustomAttribute<AvaloniaFactAttribute>() is null
                && method.GetCustomAttribute<AvaloniaTheoryAttribute>() is null
            select $"{type.Name}.{method.Name}";

        stray.ShouldBeEmpty(
            "a plain [Fact] here runs off the UI thread, and the fixture's Dispose then reaches "
            + "the dispatcher from the wrong one — which fails in whatever test is unlucky later");
    }
}
