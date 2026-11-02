namespace Flyback.Engine.Language;

/// <summary>One socket or parameter of the call a signature is about.</summary>
/// <param name="Name">As the text writes it.</param>
/// <param name="Reads">What it takes and sits at unwired, which may be empty.</param>
/// <param name="Help">What it is for, which may be empty.</param>
internal sealed record SignatureSocket(string Name, string Reads, string Help);
