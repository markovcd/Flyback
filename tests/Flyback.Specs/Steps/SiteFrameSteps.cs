using Flyback.Specs.Support;
using Flyback.Tests;
using Reqnroll;
using Reqnroll.UnitTestProvider;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The toolbar and footer every page of the website carries, checked by the script that writes them in.</summary>
[Binding]
public sealed class SiteFrameSteps(IUnitTestRuntimeProvider runtime)
{
    private (int Exit, string Printed, string Said) checkedPages;

    [When("someone checks the website's pages against its toolbar and footer")]
    public void WhenChecked() =>
        checkedPages = NodeScript.Run(runtime, "site-frame.mjs", Path.Combine(Repository.Root, "scripts", "site-frame.mjs"),
            ["--check"], Repository.Root, TimeSpan.FromSeconds(30));

    [Then("every page wears them as they are written once, in the site's partials")]
    public void ThenEveryPageWearsThem()
    {
        checkedPages.Said.ShouldBeEmpty();
        checkedPages.Printed.Trim().ShouldBeEmpty("Run node scripts/site-frame.mjs to write them in.");
        checkedPages.Exit.ShouldBe(0);
    }
}
