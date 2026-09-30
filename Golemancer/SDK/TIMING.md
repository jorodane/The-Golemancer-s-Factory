# Priority timing callbacks

`PackEngine.Contracts` exposes `EngineTiming`, `TimingStep`, `ITimingRegistry<TContext>` and optional pack capability `ITimingModuleRegistry<TContext>`. `PackEngine.Runtime.TimingScheduler<TContext>` executes callbacks. The consumer supplies its context; the engine never references a game or native window type.

## Timing points and priorities

| Conventional timing | Host invocation |
|---|---|
| `initialize` | Once after host services and callbacks are ready |
| `input` | Once before the frame's simulation steps |
| `fixed-update` | Once per fixed simulation step; zero or more times per frame |
| `update` | Once after the frame's simulation steps |
| `render-update` | Once when preparing/submitting a frame |
| `shutdown` | Once before disposing the scheduler |

These are string constants, not a closed enum. A consumer can introduce `physics`, `audio`, or another named point without changing the engine. Registering a name does not automatically create a loop: the host must call `Run` for that point. The scheduler does not create timers, own platform threads, or impose phase order.

Priorities are signed integers local to one timing point, from lowest to highest. Ordinary host work is registered at `0`; negative priorities run before it and positive priorities run after it. `0` is a convention, not a reserved slot. Equal priorities retain registration order, including after cache rebuilds. A disposed/re-registered callback receives a new registration order.

| Render-update priority | Callback |
|---:|---|
| -10 | Move/update the camera |
| 0 | Prepare/submit the native view |
| 10 | CPU work after submission returns |

`render-update` is not a GPU-completion event. WPF/Android may perform layout, paint and GPU work later. Positive priority means after the priority-zero **callback**, not after presentation to the monitor. A backend needing actual render-pass/GPU-completion hooks must invoke a separate point with the appropriate context.

```csharp
using PackEngine.Contracts;
using PackEngine.Runtime;

using var loop = new TimingScheduler<FrameState>();
using var camera = loop.Register(EngineTiming.RenderUpdate, "camera.move", -10,
    (state, step) => state.CameraX += state.Direction * 12 * step.DeltaSeconds);
using var render = loop.Register(EngineTiming.RenderUpdate, "view.submit", 0,
    (state, _) => state.Submit());

// Native-host call; FrameState/Submit are consumer-owned objects.
loop.Run(EngineTiming.RenderUpdate, frameState, deltaSeconds, elapsedSeconds);
```

Delegate targets may be host code or dynamically loaded pack implementations. The engine only orders and invokes them.

## Registration, lifetime and failures

- IDs are nonblank and unique within a timing point. Reusing an ID in another point is valid. Names use ordinal, case-sensitive comparison.
- `Register` returns `IDisposable`. Disposal unregisters immediately and releases the delegate target. Repeated disposal is harmless.
- Registration during `Run` takes effect on its next invocation. Disposing a callback not yet reached skips it in the current invocation. Self-removal is supported.
- Lists are sorted/snapshotted only after registration changes. Steady dispatch allocates no new list, sorts nothing and uses no reflection.
- Registration, dispatch and disposal belong to one host thread. Callbacks are synchronous; background-thread safety or async continuation ordering is not implied.
- Reentering the same scheduler is rejected, including another timing point. Finish the current point before dispatching the next one.
- An exception aborts remaining callbacks in that invocation. `TimingCallbackException` carries point, ID, priority and original exception. Earlier effects are not rolled back. The host decides how to pause/report/terminate.
- Scheduler disposal cancels remaining callbacks and releases targets. It does **not** dispatch shutdown implicitly. Hosts dispatch `shutdown` explicitly and dispose in `finally`, including on failure.
- Delta/elapsed seconds must be finite and nonnegative. Values are supplied by the host; the scheduler does not clamp, scale or accumulate them. All callbacks in one invocation receive the same immutable `TimingStep`.

## Pack attachment

A consumer registry can implement `ITimingModuleRegistry<TContext>` alongside its domain registry. A DLL entry point registers a configuration delegate through `Timings(configure)`. The consumer calls it once per live session, passing that session's `ITimingRegistry<TContext>`.

Create mutable callback state **inside** the configuration delegate to keep sessions independent. Keep registration handles when earlier removal is needed. Shutdown/session disposal releases remaining registrations. Do not retain a simulation/view from one session in static fields.

This optional capability changes neither `IPackModule<TRegistry>` nor existing domain registry methods. Each consumer documents its own context services and native host bindings.

## Verification

`tests/PackEngine.Verification` checks priorities, ties, custom names, registration changes during dispatch, failures/reentry, disposal and steady allocations. `tools/verify-engine.py` copies the engine and checks into a temporary directory, builds net48/net10.0 and runs the net10.0 checks without game source.
