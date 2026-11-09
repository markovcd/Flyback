using Shouldly;
using Xunit;

namespace Flyback.Assist.Tests;

/// <summary>Which assistants a host offers, and which one a fresh install starts on.</summary>
public class AssistantsTests
{
    [Fact]
    public void The_shipped_list_starts_on_chat_completions_then_gemini()
    {
        Assistants.Shipped.All.Select(a => a.Id).ShouldBe(["openai", "gemini"]);
        Assistants.Shipped.Preferred!.Id.ShouldBe("openai");
    }

    [Fact]
    public void A_host_with_none_has_nothing_to_ask()
    {
        Assistants.None.Count.ShouldBe(0);
        Assistants.None.Preferred.ShouldBeNull();
    }

    [Fact]
    public void An_assistant_is_found_by_the_id_a_setting_names()
    {
        Assistants.Shipped.Find("gemini")!.Name.ShouldBe("Gemini");
        Assistants.Shipped.Find("codex").ShouldBeNull();
    }

    /// <summary>A saved setting names an assistant by id, so two with one id would make it ambiguous.</summary>
    [Fact]
    public void Every_shipped_id_is_different()
    {
        Assistants.Shipped.All.Select(a => a.Id).ShouldBeUnique();
    }
}
