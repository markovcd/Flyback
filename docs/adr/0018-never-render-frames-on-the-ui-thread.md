# ADR-0018: Never render frames on the UI thread

**Status:** Accepted · 2026-08-11

## Context

The preview needs a frame roughly every 16 ms. A frame costs 3.5 ms at 960×540
([0006](0006-scalar-interpreter-parallel-over-rows.md)), which is comfortably
inside the budget. The obvious implementation is a `DispatcherTimer` whose tick
renders directly into the `WriteableBitmap` and invalidates.

That was the original implementation. It deadlocked the app before it ever
painted a frame — the window appeared, showed black, and never responded.

## The failure

`Process.Responding` was false with CPU time flat, so the UI thread was blocked
rather than saturated. `dotnet-stack` gave the chain exactly:

```
DispatcherTimer.FireTick
  PreviewSurface.OnTick → RenderFrame → SynthRenderer.Render
    Parallel.For → TaskReplicator.Run → Task.Wait()          ← UI thread blocks
      AvaloniaSynchronizationContext.Wait → WndProcMessageHandler   ← pumps messages
        WindowImpl.AppWndProc → TopLevel.HandlePaint               ← WM_PAINT re-enters
          CompositingRenderer.Paint → MediaContext.SyncCommit
            SyncWaitCompositorBatch → Task.Wait()                  ← waits on compositor
              NonPumpingSyncContext.Wait                           ← deadlock
```

`Parallel.For` blocks the calling thread waiting on its workers. Avalonia's
synchronisation context pumps Win32 messages while blocked — normally a good
thing. A `WM_PAINT` arrives and re-enters the renderer, which waits for a
compositor batch to commit. That batch cannot commit while the dispatcher is
inside the parallel loop, and the parallel loop cannot finish while the paint
handler holds the thread.

Neither ingredient is unusual. Any blocking parallel work on the Avalonia UI
thread can reach this.

## Decision

Frames are never rendered on the UI thread. `OnTick` snapshots what the render
needs while still on the UI thread, hands the work to `Task.Run`, and awaits it:

```csharp
var buffer = backBuffer;                        // captured on the UI thread
await Task.Run(() => renderer.Render(program, time, size.Width, size.Height, buffer, stride));
Blit(buffer, stride, size);                     // back on the UI thread
InvalidateVisual();
```

Rendering targets a plain `byte[]`; only the `Buffer.MemoryCopy` into the locked
`WriteableBitmap` touches the UI thread. A `rendering` flag drops ticks that
arrive while a frame is still in flight, rather than queueing them.

`SaveFrameAsync` is `static` and takes its inputs as parameters for the same
reason — frame export also uses `Parallel.For`, and calling it from a click
handler would have reproduced the deadlock.

## Consequences

The UI thread never blocks, so the dispatcher is free between frames and the
editor stays responsive while the preview runs.

Everything the background pass touches is snapshotted first. `CompiledPatch` is
immutable once built ([0021](0021-recompile-the-whole-patch-on-every-edit.md)),
so editing a knob mid-render cannot corrupt the frame in flight — it produces a
new program that the *next* tick picks up. The buffer, size and time are locals
captured before the `await`.

`SynthRenderer` is not thread-safe: it owns the two feedback buffers and swaps
them. The `rendering` flag is what guarantees only one render runs at a time,
and it is load-bearing rather than an optimisation. Frame export deliberately
constructs its own `SynthRenderer` so it cannot disturb the live feedback history.

Dropping late ticks means the preview degrades in frame rate rather than in
latency when a patch gets expensive, which is the right failure mode — and patch
time advances by wall-clock delta, clamped to 100 ms so a stall does not jump.

The reason is recorded in a comment on `PreviewSurface`, because "render on the
timer tick" is exactly the simplification someone would reintroduce.
