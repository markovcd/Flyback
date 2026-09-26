namespace Flyback.Plugins.Assist;

/// <summary>Where a key came from. Shown to the person, because the three differ in what they promise.</summary>
internal enum CredentialSource
{
    /// <summary>There is no key for this provider.</summary>
    None,

    /// <summary>From the environment. Flyback never wrote it and never will.</summary>
    Environment,

    /// <summary>From the operating system's own store, put there at somebody's request.</summary>
    Kept,

    /// <summary>Typed in and held for this run only. Gone when the window closes.</summary>
    Session,
}