namespace Flyback.Cli.Models;

/// <summary>One edit <c>flyback-cli tool</c> made to a patch file.</summary>
/// <param name="Tool">The tool that was called.</param>
/// <param name="Arguments">Its arguments, as the JSON they were given in.</param>
/// <param name="After">The file as the call wrote it.</param>
/// <param name="Working">
/// The patch the workbench held after the call, as patch JSON. The next call starts from
/// this rather than the file, since a <c>.fbks</c> read back gives every module a new id.
/// </param>
/// <param name="Handles">What each module answered to after the call, by handle.</param>
internal sealed record ToolStep(string Tool, string Arguments, byte[] After, string Working, Dictionary<string, Guid> Handles);
