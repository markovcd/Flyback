using System.Text.Json;
using Avalonia.Controls;

namespace Flyback.Editor.Site;

/// <summary>
/// A site's run, asked a page at a time behind More, with any earlier question
/// canceled, and saying under its heading whether it is looking, found nothing or
/// heard nothing back.
/// </summary>
/// <param name="search">Asks the site for a page, counted from 1.</param>
/// <param name="silent">What the status says when the site does not answer.</param>
internal sealed class SiteSearch<T>(
    Func<int, CancellationToken, Task<SitePage<T>>> search,
    TextBlock status,
    Button more,
    string silent) : IDisposable
{
    /// <summary>How long typing has to stop for before the site is asked.</summary>
    public static readonly TimeSpan Typing = TimeSpan.FromMilliseconds(250);

    private int page;
    private CancellationTokenSource? asking;

    /// <summary>Lists a page's items, emptying the list first when the page is a fresh first one.</summary>
    public required Action<IReadOnlyList<T>, bool, CancellationToken> Show { get; init; }

    /// <summary>Whether nothing is listed, after a page has been shown.</summary>
    public required Func<bool> Empty { get; init; }

    /// <summary>What the status says when nothing is listed.</summary>
    public required Func<string> Nothing { get; init; }

    /// <summary>What else happens when the site does not answer, told whether the question was a fresh one.</summary>
    public Action<bool, CancellationToken>? Failed { get; init; }

    /// <summary>
    /// Asks for the first page again, or for the page after the last, once
    /// <paramref name="after"/> has passed without another question.
    /// </summary>
    public async Task AskAsync(bool fresh, TimeSpan after = default)
    {
        Stop();

        var cancel = (asking = new CancellationTokenSource()).Token;
        var wanted = fresh ? 1 : page + 1;

        if (fresh) status.Text = "Looking…";

        status.IsVisible = fresh;
        more.IsEnabled = false;

        SitePage<T> found;

        try
        {
            if (after > TimeSpan.Zero) await Task.Delay(after, cancel);

            found = await search(wanted, cancel);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            status.Text = silent;
            status.IsVisible = true;
            more.IsEnabled = true;

            Failed?.Invoke(fresh, cancel);
            return;
        }

        if (cancel.IsCancellationRequested) return;

        page = found.Page;

        Show(found.Items, fresh, cancel);

        status.Text = Nothing();
        status.IsVisible = Empty();
        more.IsVisible = found.More;
        more.IsEnabled = true;
    }

    /// <summary>Cancels the question on its way, if there is one.</summary>
    public void Stop()
    {
        asking?.Cancel();
        asking?.Dispose();
        asking = null;
    }

    public void Dispose() => Stop();
}
