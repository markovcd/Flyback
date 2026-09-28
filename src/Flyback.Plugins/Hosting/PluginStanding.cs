namespace Flyback.Plugins.Hosting;

/// <summary>Why a plugin folder loads, or why it does not.</summary>
internal enum PluginStanding
{
    /// <summary>A build that checks nothing: Debug, and All plugins beside it.</summary>
    Unchecked,

    /// <summary>Built with this copy of Flyback, as <see cref="ShippedList"/> says.</summary>
    Shipped,

    /// <summary>Somebody said yes to it as it is.</summary>
    Allowed,

    /// <summary>A file in it is not as it was when it shipped or was allowed.</summary>
    Changed,

    /// <summary>Nobody said yes to it.</summary>
    NotAllowed,
}
