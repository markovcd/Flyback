using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;
using Reqnroll;
using Shouldly;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>The effort picker on the shipped assistants' forms.</summary>
[Binding]
public sealed class AssistantEffortSteps
{
    private SettingValues values = SettingValues.None;
    private SettingField? effort;

    private SettingField Effort => effort.ShouldNotBeNull();

    [Given("a probe has measured how long {word} thinks")]
    public void GivenMeasured(string model) =>
        values = values
            .With(AssistantSchema.ModelKey, model)
            .With(Survey.Key, Survey.Write([new ModelReport(model) { Hearing = true, Least = 128, Most = 32768 }]));

    [When("^the (.+) assistant's settings are opened$")]
    public void WhenOpened(string name)
    {
        var assistant = ShippedPlugins.Loaded
            .Assistants.Single(a => a.Name == name);

        effort = assistant.Form(values).Single(f => f.Key == AssistantSchema.EffortKey);
    }

    [Then("the effort setting cannot be changed")]
    public void ThenGrayed() => Effort.Enabled.ShouldBeFalse();

    [Then("the effort setting can be changed")]
    public void ThenLive() => Effort.Enabled.ShouldBeTrue();

    [Then("the effort setting says why")]
    public void ThenSaysWhy() => Effort.Because.ShouldNotBeNullOrWhiteSpace();
}
