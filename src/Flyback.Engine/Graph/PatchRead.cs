namespace Flyback.Core.Graph;

/// <summary>What reading a document came to; <see cref="PatchOpen"/> without the files.</summary>
public readonly record struct PatchRead(Patch? Patch, IReadOnlyList<string> Problems);