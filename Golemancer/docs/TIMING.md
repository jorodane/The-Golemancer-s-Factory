# Game timing callbacks

The frozen PackEngine SDK provides an ordered timing scheduler. The game supplies its domain context and connects native frame notifications to it. This engine upgrade is additive: existing `IGameModule` entry points, `IModuleRegistry`, domain ABI `contracts="2"`, and JSON save schema 1 remain valid.

## Register from a pack DLL

```csharp
using Golemancer.Contracts;
using EngineTiming = PackEngine.Contracts.EngineTiming;

public sealed class ViewModule : IGameModule
{
    public void Register(IModuleRegistry registry)
    {
        if (registry is not PackEngine.Contracts.ITimingModuleRegistry<IGameTimingContext> timing)
            throw new InvalidOperationException("This pack requires timing callback support.");
        timing.Timings(callbacks =>
        {
            // Mutable variables declared here belong to one session.
            callbacks.Register(EngineTiming.RenderUpdate, "example.view", -5, (context, step) =>
            {
                if (context.Camera is not { } camera) return; // Headless session.
                // Read/update camera.X, camera.Y, camera.Zoom here.
                // Native camera movement is -10, native frame submission is 0.
            });
        });
    }
}
```

This is a contract usage snippet, not an additional installed example pack. Compile against the local game contracts and SDK as usual. The existing `<Assembly>` manifest entry loads `Register` dynamically. Qualified engine types/aliases also avoid ambiguity between the legacy net48 dictionary extension classes in the two contract namespaces.

`IGameTimingContext.Game` is the current `IGameContext`; read it on each call because loading or starting a game replaces the world. `Camera` is an optional native `IGameCamera` whose X/Y use world tiles and Zoom uses pixels per tile. `Paused` indicates suspended gameplay. Camera hooks must supply finite coordinates and positive zoom. Use a priority such as `-5` for effects after native camera movement and before submission.

## Host bindings

| Point | Windows | Android |
|---|---|---|
| `initialize` | First frame, after host services/callbacks are bound | First frame, after host services/callbacks are bound |
| `input`, priority 0 | Keyboard/device input | Touch/gamepad input |
| `fixed-update`, priority 0 | `GameSession.AdvanceCore`, 1/60 s per step | `GameSession.AdvanceCore`, existing 0.05 s per step |
| `update`, priority 0 | HUD, dialogue, facility overlays | Notices |
| `render-update`, priority -10 | Keyboard camera movement/combat tracking | Camera movement/tracking and bounds |
| `render-update`, priority 0 | Request WPF world redraw | Request native Skia view redraw |
| `shutdown` | Window close/session disposal | Activity/view disposal |

Each native frame runs Input, zero or more FixedUpdate steps, Update, then RenderUpdate. The simulation callback is `golemancer.simulation`; other built-ins are `windows.input`, `windows.hud`, `windows.camera`, `windows.render`, and `android.input`, `android.notices`, `android.camera`, `android.render`. Duplicate IDs within one point are rejected.

The session attaches pack factories after host callbacks are installed and before its first timing dispatch. Initialize/Shutdown run at most once per normal session lifecycle. `GameSession.Dispose` runs Shutdown and releases callbacks in `finally`. New-game/load replaces the context's Game without creating another host session or re-running Initialize. A headless caller can call `StartTimings`; the first `Advance` also initializes timing automatically.

Paused/menu/inactive/dialogue states skip **all FixedUpdate callbacks**. Frame/UI callbacks can continue and should use `context.Paused` where appropriate. Gameplay belongs in FixedUpdate; view effects belong in RenderUpdate. Direct `Simulation.Tick` does not run host/session timing callbacks, preserving detached projections and direct simulation tests.

For frame phases, `ElapsedSeconds` is the native host's monotonic time; for FixedUpdate it is simulation time at the start of that step. Initialize receives zero time and Shutdown receives final simulation time. Do not compare clocks between different points.

Windows uses `CompositionTarget.Rendering`/`RenderingTime` instead of a 16 ms timer. Duplicate composition timestamps are ignored. Simulation remains 60 Hz at any display rate; camera input uses the display frame's delta. Combat tracking uses a time-based weight preserving its old 60 Hz response. Activation, minimization, and new-game/load reset the clock. Long stalls retain bounded catch-up (0.25 s simulation, 0.1 s camera).

Android retains Choreographer and its existing simulation step. Both hosts use the same scheduler and priorities. `render-update` orders CPU preparation/submission, not GPU completion.

Callback failures stop the host frame loop and report timing/ID/priority. Windows records details in `%LOCALAPPDATA%/GolemancerFactory/timing-error.txt`; Android uses logcat. Earlier callback effects are not rolled back.

See the SDK [timing contract](../SDK/TIMING.md) for ordering, mutation, lifetime and custom names. Custom points require a host/driver invocation; registration alone does not cause dispatch.
