namespace Flyback.App.Statistics;

/// <summary>How a run began — each a yes or a no, and none of them about who began it.</summary>
/// <param name="First">No settings folder existed: the first start on this machine, which is the nearest thing to an install that can be counted without keeping an id.</param>
/// <param name="Updated">A release Flyback downloaded itself installed just before this start.</param>
/// <param name="File">It was started to open a file.</param>
public sealed record Launch(bool First = false, bool Updated = false, bool File = false);