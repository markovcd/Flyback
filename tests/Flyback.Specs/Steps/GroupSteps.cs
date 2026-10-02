using Avalonia;
using Avalonia.Input;
using Reqnroll;
using Shouldly;
using Flyback.Editor.Canvas;
using Flyback.Core.Graph;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>Which modules a group holds, and how that changes on the canvas.</summary>
[Binding]
public sealed class GroupSteps(PatchContext context, EditorDriver editor)
{
    private NodeGroup? box;

    [Given("an open group {string} of three modules in a row")]
    public void GivenAnOpenGroup(string name)
    {
        var first = Place("first", NodeCatalog.SineTypeId, 0, 0);
        var middle = Place("middle", "math.mul", 300, 0);
        var last = Place("last", "math.mul", 600, 0);

        context.Wire("first", "out", "middle", "a");
        context.Wire("middle", "out", "last", "a");

        var group = context.Patch.Group([first.Id, middle.Id, last.Id]).ShouldNotBeNull();

        group.Name = name;
        group.Collapsed = false;
    }

    [Given("a Time below it, in no group")]
    public void GivenALooseTime() => Place("Time", NodeCatalog.TimeTypeId, 0, 400);

    [Given("a group of two modules with no name")]
    public void GivenAnUnnamedGroup()
    {
        var left = Place("left", NodeCatalog.SineTypeId, 0, 700);
        var right = Place("right", "math.mul", 300, 700);

        context.Patch.Group([left.Id, right.Id]).ShouldNotBeNull();
    }

    [When("{string}, the group with no name and the Time are grouped")]
    public void WhenGroupedWithTheUnnamedGroup(string name)
    {
        editor.Select([.. Group(name).Members, context.Node("left").Id, context.Node("right").Id, context.Node("Time").Id]);
        editor.PressCtrl(PhysicalKey.G);
    }

    [Given("the Time feeds an Expression drawn in a box with a Multiply")]
    public void GivenAnExpressionInABox()
    {
        var expression = Place("Expression", NodeCatalog.ExpressionTypeId, 0, 700);
        var multiply = Place("Multiply", "math.mul", 300, 700);

        context.Wire("Time", "t", "Expression", "a");
        context.Wire("Expression", "out", "Multiply", "a");

        editor.Select(expression.Id, multiply.Id);
        editor.PressCtrl(PhysicalKey.G);

        box = context.Patch.GroupOf(expression.Id).ShouldNotBeNull();
    }

    [When("the list is opened inside {string}")]
    public void WhenTheListIsOpenedInside(string name) => editor.RightClick(Inside);

    [When("the Time is carried into {string} with Shift held")]
    public void WhenCarriedInWithShift(string name) => editor.Carry(context.Node("Time").Id, Inside, RawInputModifiers.Shift);

    [When("the Time is carried into {string}")]
    public void WhenCarriedIn(string name) => editor.Carry(context.Node("Time").Id, Inside);

    [When("the middle module is carried out of {string} with Shift held")]
    public void WhenCarriedOut(string name) => editor.Carry(context.Node("middle").Id, new Point(400, 500), RawInputModifiers.Shift);

    [When("{string} and the Time are grouped")]
    public void WhenGroupedWithTheTime(string name)
    {
        editor.Select([.. Group(name).Members, context.Node("Time").Id]);
        editor.PressCtrl(PhysicalKey.G);
    }

    [Then("{string} holds {int} modules")]
    public void ThenHolds(string name, int count) => Group(name).Members.Count.ShouldBe(count);

    [Then("{string} holds the Time")]
    public void ThenHoldsTheTime(string name) => Group(name).Members.ShouldContain(context.Node("Time").Id);

    [Then("{string} does not hold the Time")]
    public void ThenDoesNotHoldTheTime(string name) => Group(name).Members.ShouldNotContain(context.Node("Time").Id);

    [Then("the middle module is in no group")]
    public void ThenTheMiddleIsLoose() => context.Patch.GroupOf(context.Node("middle").Id).ShouldBeNull();

    [Then("the box's edge reads {string} where the Time's wire arrives")]
    public void ThenTheEdgeReads(string label)
    {
        var group = box.ShouldNotBeNull();
        var arriving = new GroupSocket(context.Node("Expression").Id, 0, IsOutput: false);

        editor.Read(canvas => canvas.Selection.Scene.Named(arriving)?.Label).ShouldBe(label);
        context.Patch.SocketsOf(group).Inputs.ShouldContain(arriving);
    }

    /// <summary>Inside the ring, between the first two modules.</summary>
    private static Point Inside => new(NodeGeometry.Width + (300 - NodeGeometry.Width) / 2, 40);

    private NodeInstance Place(string name, string typeId, double x, double y)
    {
        var node = context.Add(name, typeId);

        node.X = x;
        node.Y = y;
        return node;
    }

    private NodeGroup Group(string name) =>
        context.Patch.Groups.ShouldNotBeNull().Single(group => group.Name == name);
}
