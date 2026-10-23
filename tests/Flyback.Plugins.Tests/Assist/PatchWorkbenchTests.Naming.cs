using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Assist;

public partial class PatchWorkbenchTests
{
    // --- naming things ------------------------------------------------------

    [Fact]
    public async Task A_module_written_without_a_name_is_given_a_handle()
    {
        var added = await Write(Bench(), "sine() |> out.color");

        added.Ok.ShouldBeTrue(added.Text);
        added.Text.ShouldContain("sine1");
    }

    [Fact]
    public async Task Two_of_the_same_module_get_different_handles()
    {
        var added = await Write(Bench(), "sine() |> out.color\nsine() |> out.left");

        added.Text.ShouldContain("sine1");
        added.Text.ShouldContain("sine2");
    }

    [Fact]
    public async Task A_name_already_in_use_is_refused()
    {
        var bench = Bench();

        var second = await Write(bench, "let a = sine()\nlet a = saw()");

        second.Ok.ShouldBeFalse();
        second.Text.ShouldContain("already");
    }

    [Fact]
    public async Task A_handle_the_model_chose_is_the_name_describe_patch_prints()
    {
        var bench = Bench();

        await Write(bench, "let glow = value()\nglow |> out.color");

        (await Call(bench, "describe_patch")).Text.ShouldContain("let glow = ");
    }
}
