namespace Flyback.Core.Compile;

/// <summary>What a run asks of its <see cref="IlCompiler"/>.</summary>
public interface IIlCompilerSetup
{
    /// <summary>Keep every program on the interpreter.</summary>
    bool Interpreted { get; }
}