using System.Net;

namespace Flyback.Plugins.Testing;

/// <summary>One answer a <see cref="Canned"/> endpoint gives, with whatever the endpoint would have said around it.</summary>
public sealed record CannedAnswer(
    string Body,
    HttpStatusCode Status = HttpStatusCode.OK,
    (string Name, string Value)[]? Headers = null);
