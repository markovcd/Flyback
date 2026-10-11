using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Shouldly;

namespace Flyback.Specs.Support;

/// <summary>
/// One of <see cref="Chromium"/>'s pages over the DevTools protocol: sent somewhere,
/// asked things in its own JavaScript, and touched as a person would.
/// </summary>
internal sealed class BrowserPage : IDisposable
{
    /// <summary>How long one call may take; the slowest, a script awaiting a preset opening, takes seconds.</summary>
    private static readonly TimeSpan Cap = TimeSpan.FromMinutes(1);

    /// <summary>How many of the page's last words a failure quotes.</summary>
    private const int Quoted = 20;

    private readonly ClientWebSocket socket;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> waiting = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> expected = new();
    private readonly ConcurrentQueue<string> said = new();
    private readonly CancellationTokenSource closing = new();
    private int sent;
    private int mediaPlayers;

    private BrowserPage(ClientWebSocket socket)
    {
        this.socket = socket;
        _ = Task.Run(Read);
    }

    public static BrowserPage Connect(Uri debugger)
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.Zero;
        socket.ConnectAsync(debugger, CancellationToken.None).Wait(Cap).ShouldBeTrue($"The browser's page did not take a connection within {Cap.TotalSeconds:0} s.");

        var page = new BrowserPage(socket);
        page.Send("Runtime.enable");
        page.Send("Page.enable");
        page.Send("Media.enable");

        return page;
    }

    /// <summary>How many media players the page has made: one for each audio or video element that played.</summary>
    public int MediaPlayers => mediaPlayers;

    /// <summary>Has the page take the browser for the one <paramref name="userAgent"/> names, from the next address it goes to.</summary>
    public void Pretend(string userAgent) => Send("Emulation.setUserAgentOverride", new JsonObject { ["userAgent"] = userAgent });

    /// <summary>Sends the page to <paramref name="address"/> and waits for its document to load.</summary>
    public void Go(Uri address)
    {
        var loaded = Expect("Page.loadEventFired");
        var answer = Send("Page.navigate", new JsonObject { ["url"] = address.AbsoluteUri });

        if (answer["errorText"] is { } failed) throw new InvalidOperationException($"{address} did not open: {failed}");

        loaded.Wait(Cap).ShouldBeTrue($"{address} did not finish loading within {Cap.TotalSeconds:0} s.{Words()}");
    }

    /// <summary>The value of <paramref name="expression"/> in the page, a promise awaited, as JSON.</summary>
    /// <exception cref="InvalidOperationException">The expression threw, with what it threw and what the page last said.</exception>
    public JsonNode? Evaluate(string expression)
    {
        var answer = Send("Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression,
            ["awaitPromise"] = true,
            ["returnByValue"] = true,
        });

        if (answer["exceptionDetails"] is { } thrown)
        {
            var why = (string?)thrown["exception"]?["description"] ?? (string?)thrown["text"];
            throw new InvalidOperationException($"`{expression}` threw: {why}{Words()}");
        }

        return answer["result"]!["value"]?.DeepClone();
    }

    /// <summary>Waits for <paramref name="expression"/> to be true in the page, failing with <paramref name="what"/> after <paramref name="cap"/>.</summary>
    /// <remarks>An expression that throws counts as not yet: the page's own scripts may not have defined what it reads.</remarks>
    public void Until(string expression, string what, TimeSpan cap)
    {
        var deadline = DateTime.UtcNow + cap;
        var asked = $"(() => {{ try {{ return Boolean({expression}); }} catch {{ return false; }} }})()";

        while ((bool?)Evaluate(asked) != true)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"{what} within {cap.TotalSeconds:0} s: `{expression}` stayed false.{Words()}");

            Thread.Sleep(50);
        }
    }

    /// <summary>A finger pressed at (<paramref name="x"/>, <paramref name="y"/>) in the page's pixels, moved by (<paramref name="dx"/>, <paramref name="dy"/>), and lifted.</summary>
    /// <remarks>
    /// Each touch carries when it happened, as a touchscreen's does. Unstamped, Chrome stamps it on arrival,
    /// and a page still busy with the press makes a quick tap look like a long one.
    /// </remarks>
    public void Touch(double x, double y, double dx = 0, double dy = 0)
    {
        Send("Emulation.setTouchEmulationEnabled", new JsonObject { ["enabled"] = true, ["maxTouchPoints"] = 1 });

        var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        Send("Input.dispatchTouchEvent", Finger("touchStart", x, y, at));
        if (dx != 0 || dy != 0) Send("Input.dispatchTouchEvent", Finger("touchMove", x + dx, y + dy, at + 0.05));
        Send("Input.dispatchTouchEvent", new JsonObject { ["type"] = "touchEnd", ["touchPoints"] = new JsonArray(), ["timestamp"] = at + 0.1 });
    }

    /// <summary>The left mouse button pressed at (<paramref name="x"/>, <paramref name="y"/>), dragged by (<paramref name="dx"/>, <paramref name="dy"/>) in steps, and released.</summary>
    public void Drag(double x, double y, double dx, double dy)
    {
        const int steps = 8;

        Send("Input.dispatchMouseEvent", Mouse("mousePressed", x, y));
        for (var step = 1; step <= steps; step++)
            Send("Input.dispatchMouseEvent", Mouse("mouseMoved", x + dx * step / steps, y + dy * step / steps));
        Send("Input.dispatchMouseEvent", Mouse("mouseReleased", x + dx, y + dy));
    }

    private static JsonObject Mouse(string type, double x, double y) => new()
    {
        ["type"] = type,
        ["x"] = x,
        ["y"] = y,
        ["button"] = "left",
        ["buttons"] = type == "mouseReleased" ? 0 : 1,
        ["clickCount"] = 1,
    };

    private static JsonObject Finger(string type, double x, double y, double at) => new()
    {
        ["type"] = type,
        ["touchPoints"] = new JsonArray(new JsonObject { ["x"] = x, ["y"] = y, ["id"] = 1 }),
        ["timestamp"] = at,
    };

    /// <summary>Calls <paramref name="method"/>, and answers its result.</summary>
    /// <exception cref="InvalidOperationException">The browser refused the call, with why.</exception>
    public JsonObject Send(string method, JsonObject? parameters = null)
    {
        var id = Interlocked.Increment(ref sent);
        var answer = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        waiting[id] = answer;

        var message = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters ?? [] };
        lock (socket)
            socket.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None).Wait(Cap);

        if (!answer.Task.Wait(Cap))
        {
            waiting.TryRemove(id, out _);
            throw new TimeoutException($"{method} had no answer within {Cap.TotalSeconds:0} s.{Words()}");
        }

        var reply = answer.Task.Result;

        return reply["error"] is { } error
            ? throw new InvalidOperationException($"{method} was refused: {error["message"]}")
            : reply["result"]!.AsObject();
    }

    /// <summary>Resolves when the page next sends <paramref name="method"/>.</summary>
    private Task Expect(string method)
    {
        var heard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        expected[method] = heard;
        return heard.Task;
    }

    private async Task Read()
    {
        var buffer = new byte[1 << 16];
        var message = new MemoryStream();

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var received = await socket.ReceiveAsync(buffer, closing.Token);
                if (received.MessageType == WebSocketMessageType.Close) break;

                message.Write(buffer, 0, received.Count);
                if (!received.EndOfMessage) continue;

                Take(JsonNode.Parse(message.ToArray())!.AsObject());
                message.SetLength(0);
            }
        }
        catch (Exception failure) when (failure is OperationCanceledException or WebSocketException)
        {
            // Closed by Dispose, or the browser ended: whoever is waiting times out saying so.
        }
    }

    private void Take(JsonObject message)
    {
        if (message["id"] is { } id)
        {
            if (waiting.TryRemove((int)id, out var answer)) answer.SetResult(message);
            return;
        }

        var method = (string?)message["method"];
        var parameters = message["params"];

        if (method is not null && expected.TryRemove(method, out var heard)) heard.SetResult();

        switch (method)
        {
            case "Runtime.consoleAPICalled":
                var words = parameters!["args"]!.AsArray().Select(a => a!["value"]?.ToString() ?? (string?)a["description"]);
                Remember($"console.{parameters["type"]}: {string.Join(' ', words)}");
                break;

            case "Media.playerCreated":
                Interlocked.Increment(ref mediaPlayers);
                break;

            case "Runtime.exceptionThrown":
                var details = parameters!["exceptionDetails"]!;
                Remember($"uncaught: {(string?)details["exception"]?["description"] ?? (string?)details["text"]}");
                break;
        }
    }

    private void Remember(string line)
    {
        said.Enqueue(line);
        while (said.Count > 200) said.TryDequeue(out _);
    }

    /// <summary>The page's last words, for a failure to quote.</summary>
    private string Words()
    {
        var last = said.TakeLast(Quoted).ToList();
        return last.Count == 0 ? " The page said nothing." : $" The page last said:\n  {string.Join("\n  ", last)}";
    }

    public void Dispose()
    {
        closing.Cancel();
        socket.Abort();
        socket.Dispose();
        closing.Dispose();
    }
}
