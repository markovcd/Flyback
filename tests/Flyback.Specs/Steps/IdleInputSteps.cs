using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>Inputs a module's formula never reads, drawn faint on the canvas.</summary>
[Binding]
public sealed class IdleInputSteps(PatchContext context, EditorDriver editor)
{
    private NodeInstance Expression => context.Node("Expression");

    private int Port(string name) => NodeCatalog.BuiltIn.Require(Expression.TypeId).Inputs.ToList().FindIndex(port => port.Name == name);

    [Given("an Expression whose formula is {string}")]
    public void GivenAnExpression(string formula)
    {
        var node = context.Add("Expression", NodeCatalog.ExpressionTypeId);
        node.SetState("expression", new JsonObject { ["formula"] = formula });
    }

    [Then("the Expression's {string} is drawn faint with no well")]
    public void ThenFaint(string port)
    {
        editor.InputIdle(Expression.Id, Port(port)).ShouldBeTrue();
        editor.HasWell(Expression.Id, Port(port)).ShouldBeFalse();
    }

    [Then("the Expression's {string} is drawn in full with a well")]
    public void ThenFull(string port)
    {
        editor.InputIdle(Expression.Id, Port(port)).ShouldBeFalse();
        editor.HasWell(Expression.Id, Port(port)).ShouldBeTrue();
    }
}
