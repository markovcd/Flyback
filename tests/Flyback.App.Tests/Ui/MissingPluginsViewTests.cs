using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Flyback.App;
using Flyback.App.Controls;
using Flyback.App.PluginPackages;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>What a patch is short of, offered rather than installed.</summary>
public sealed class MissingPluginsViewTests : UiTest
{
    private static SitePlugin Listed(string id, string name, string author = "") => new(
        id,
        new ListedPlugin($"Flyback.Plugins.{name}", name, "1.0.0", author, string.Empty, [], []),
        ["win"],
        Sha256: string.Empty,
        Size: 0,
        Downloads: 0,
        new Uri("http://site.test/file"),
        Preview: null,
        SiteRating.None);

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void Every_plugin_found_for_it_is_named()
    {
        var view = MissingPluginsView.View([Listed("a1", "Ripples", "Ann"), Listed("b1", "Grain")]);
        var window = Show(view, width: 520);

        Settle(window);

        All<TextBlock>(view).Single(t => t.Name == "missingSummary").Text.ShouldNotBeNull().ShouldContain("2 plugins");
        All<StackPanel>(view).Single(p => p.Name == "missingList").Children.Count.ShouldBe(2);
    }

    [AvaloniaFact]
    public void Finding_it_is_answered_yes_and_not_now_is_answered_no()
    {
        foreach (var (name, expected) in new[] { ("findPlugins", true), ("notNow", false) })
        {
            // Something with a size of its own: a dialog goes over the window, and an
            // empty one has no room to put it in.
            var window = Show(new Border { Width = 600, Height = 400 }, width: 600);
            var asked = window.ShowDialog<bool>(MissingPluginsView.Title, MissingPluginsView.View([Listed("a1", "Ripples")]));

            Settle(window);
            Press(All<Button>(window).Single(b => b.Name == name));
            Settle(window);

            asked.IsCompletedSuccessfully.ShouldBeTrue();
            asked.Result.ShouldBe(expected);
        }
    }
}
