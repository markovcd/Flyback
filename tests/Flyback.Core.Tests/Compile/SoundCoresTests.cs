using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>Settings → Sound's Cores: how many legs a sound built from now on may be cut into.</summary>
[Collection(SoundCoresOneAtATime.Name)]
public sealed class SoundCoresTests : IDisposable
{
    private readonly int before = SoundCores.Wanted;

    public void Dispose() => SoundCores.Wanted = before;

    [Theory]
    [InlineData(-1)]
    [InlineData(SoundCores.Highest + 1)]
    public void A_count_not_on_offer_is_left_to_the_machine(int asked)
    {
        SoundCores.Wanted = asked;

        SoundCores.Wanted.ShouldBe(SoundCores.Automatic);
        SoundCores.Most.ShouldBe(SoundCores.Here);
    }

    [Fact]
    public async Task Asking_for_other_cores_cuts_the_playing_sound_again()
    {
        SoundCores.Wanted = 1;
        using var compiler = new IlCompiler();
        var program = Heaviest();

        compiler.Submit(program, IlLane.Sound);
        await compiler.Settled();
        program.Il.ShouldNotBeNull().Legs.ShouldBeNull();

        SoundCores.Wanted = 2;
        await compiler.Settled();
        program.Il.ShouldNotBeNull().Legs.ShouldNotBeNull().Count.ShouldBe(2);

        SoundCores.Wanted = 1;
        await compiler.Settled();
        program.Il.ShouldNotBeNull().Legs.ShouldBeNull();
    }

    private static CompiledPatch Heaviest() =>
        Presets.All
            .Select(preset => preset.Build(NodeCatalog.Current).CompileForAudio().Program)
            .MaxBy(program => program.Ops.Length)!;
}
