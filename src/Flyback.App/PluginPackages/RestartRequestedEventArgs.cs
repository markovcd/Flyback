namespace Flyback.App.PluginPackages;

internal sealed class RestartRequestedEventArgs(Reopen? reopen) : EventArgs
{
    private readonly TaskCompletionSource<bool> result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Reopen? Reopen { get; } = reopen;

    public Task<bool> Result => result.Task;

    public void Complete(bool restarted) => result.TrySetResult(restarted);

    public void Fail(Exception error) => result.TrySetException(error);
}
