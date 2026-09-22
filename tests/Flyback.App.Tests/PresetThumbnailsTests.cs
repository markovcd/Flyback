using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests;

/// <summary>The stills the gallery's tiles are drawn with.</summary>
public class PresetThumbnailsTests
{
    /// <summary>A tile drawn from IL is the tile the interpreter draws, to the byte.</summary>
    [Fact]
    public async Task A_compiled_still_is_the_interpreted_one()
    {
        var preset = Presets.All.First(preset => preset.Build(NodeCatalog.BuiltIn).Reaches().Picture);

        using var compiler = new IlCompiler();
        var compiled = await new PresetThumbnails(NodeCatalog.BuiltIn, compiler).Of(preset);
        var interpreted = await new PresetThumbnails(NodeCatalog.BuiltIn).Of(preset);

        compiled.Pixels.ShouldNotBeNull().ShouldBe(interpreted.Pixels);
    }
}
