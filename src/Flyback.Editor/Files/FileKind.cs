namespace Flyback.App.Files;

/// <summary>One of the kinds of file Flyback opens, as the operating system is told about it.</summary>
/// <param name="Extension">With its dot.</param>
/// <param name="ProgId">What Windows files it under.</param>
/// <param name="MimeType">What a Linux desktop files it under.</param>
/// <param name="Icon">Its icon's name in <see cref="FileTypes.IconFolder"/>, without an extension.</param>
internal sealed record FileKind(string Extension, string Name, string ProgId, string MimeType, string Icon);