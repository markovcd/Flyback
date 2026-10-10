using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Editor.Tests.Inspect;

/// <summary>With nothing selected the panel is headed with the patch's name, the file it is.</summary>
public class PatchHeaderTests : EditorTest
{
    private static string Heading(Window window) =>
        All<TextBlock>(window).Single(t => t.Name == "patch-name").Text!;

    [AvaloniaFact]
    public void A_patch_with_no_file_is_headed_Patch()
    {
        var window = Open(new PatchBuilder(NodeCatalog.BuiltIn).Patch);

        Service<PatchFiles>(window).Became(null, beside: null);
        Settle(window);

        Heading(window).ShouldBe("PATCH");
    }

    [AvaloniaFact]
    public void A_patch_from_a_file_is_headed_with_the_files_name_in_capitals()
    {
        var window = Open(new PatchBuilder(NodeCatalog.BuiltIn).Patch);

        Service<PatchFiles>(window).Became("nebula", beside: null);
        Settle(window);

        Heading(window).ShouldBe("NEBULA");
    }
}
