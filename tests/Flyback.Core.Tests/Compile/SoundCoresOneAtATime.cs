namespace Flyback.Core.Tests.Compile;

/// <summary>Tests that change <see cref="Engine.Render.SoundCores.Wanted"/>, which the whole process reads, one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SoundCoresOneAtATime
{
    public const string Name = "Sound cores";
}
