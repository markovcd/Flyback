using Flyback.Specs.Support;
using Reqnroll;
using Reqnroll.UnitTestProvider;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The editor's menus along the top of the window, off a Mac.</summary>
[Binding]
public sealed class MenuBarSteps(EditorDriver editor, IUnitTestRuntimeProvider runtime)
{
    [Given("the editor is on Windows or Linux")]
    public void GivenNotAMac()
    {
        if (OperatingSystem.IsMacOS()) runtime.TestIgnore("a Mac's menus are in its own menu bar");
    }

    [Then("the window has no menu bar")]
    public void ThenNoMenuBar() => editor.MenuBar.ShouldBeEmpty();

    [Given("the menu bar is switched on")]
    [When("the menu bar is switched on")]
    public void WhenTheMenuBarIsSwitchedOn()
    {
        editor.OpenSettings("Canvas");
        editor.Tick("Show a menu bar", on: true);
        editor.Answer("Save");
    }

    [Then("the menu bar holds File, Edit, View, Play and Help")]
    public void ThenTheMenuBarHoldsTheMenus() => editor.MenuBar.ShouldBe(["File", "Edit", "View", "Play", "Help"]);

    [When("{word} is chosen from the {word} menu")]
    public void WhenChosen(string item, string menu) => editor.ChooseFromMenuBar(menu, item);
}
