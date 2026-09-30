# Engine rendering camera

`PackEngine.Contracts.Rendering.ICamera2D` is the pack-facing camera API.
`PackEngine.Runtime.Rendering.Camera2D` owns position, zoom and their targets.
It has no game, tile, input-device or native-window dependency. A headless
consumer does not need to create a camera.

| Owner | Responsibility |
|---|---|
| Engine camera | State, time-based interpolation, immutable view, projection and inverse projection |
| Consumer controller | Tracking targets, world limits, input bindings, zoom range, transition response |
| Native renderer | Viewport/device scaling, drawing, resource caches, last-drawn view used by picking |

## Units and frames

World units are defined by the consumer. Zoom is **logical viewport units per
world unit**, not a fixed tile size or a device-pixel count. Screen origin is
top-left, with positive X right and positive Y down. A view is orthographic,
axis-aligned and centered at `(X, Y)`; it does not support camera rotation.
Viewport dimensions must be finite and nonnegative; zoom must be finite and
positive. `default(CameraFrame2D)` is invalid; capture a view before projecting.

`Capture(width, height)` creates an allocation-free immutable `CameraFrame2D`.
It provides `WorldToScreen`, `ScreenToWorld`, visible bounds and point containment.
Use one captured view for every world draw/culling operation in that paint.
Retain it for pointer conversion until the next paint, including while camera
state or the viewport is changing. Native device-pixel scaling is applied outside
this transform. A captured/drawn view is not evidence of GPU presentation.

## Control and ordering

- `MoveTo(x, y, response)` and `ZoomTo(zoom, response)` change only targets.
  Repeated input accumulates on `TargetX`, `TargetY` or `TargetZoom`.
- The owner calls `Advance(deltaSeconds)` once at `render-update -10`, even if
  game simulation is paused. The camera creates no timer or scheduler registration.
- Positive response is exponential decay in inverse seconds; for a constant
  target, splitting elapsed time into different frame rates gives the same result.
  Zoom defaults to response `18`. Response zero applies the target at the next
  `Advance`, including a zero-delta call. Deltas must be finite and nonnegative;
  the host owns pause/resume clamping.
- Direct `X`, `Y` or `Zoom` writes snap that component and its target, preserving
  existing imperative callers. `Snap(x, y, zoom)` cancels all pending transitions.
- Native submission remains at priority `0`. Deferred WPF/Android paint captures
  camera state when it actually draws; picking keeps the preceding view until then.
  Later CPU callbacks are not GPU-completion callbacks.
- Camera instances belong to one host thread/session. No thread safety or
  automatic tracking of consumer objects is implied.

```csharp
ICamera2D camera = new Camera2D(10, 20, 52);
// Input: consumer supplies its own limits and bindings.
camera.ZoomTo(Math.Min(96, camera.TargetZoom + wheelSteps * 2));
// Render preparation, once per frame:
camera.Advance(deltaSeconds);
// Native paint:
CameraFrame2D drawnView = camera.Capture(logicalWidth, logicalHeight);
CameraPoint2D screen = drawnView.WorldToScreen(worldX, worldY);
// Pointer picking before the next paint uses drawnView, not a fresh capture.
```

## Resource caches

Camera changes transform existing world images. They do not inherently invalidate
terrain or sprite pixels. Consumers own cache quality/memory policy independently
of the camera; newly visible or edited regions may need work. Golemancer keeps
its Windows terrain cache at 64 texels/world-unit and Android at 32 across zoom
changes. Background refinement of new/dirty regions remains enabled.

## Verification

Engine-only checks cover fractional/inverse coordinates, old-frame picking,
resize, 30/60/75/120/144 Hz interpolation, input accumulation/reversal, resets,
validation and steady allocations. Consumer tests exercise the engine camera
through timing/session APIs. Native smoke tests exercise actual zoom input,
last-drawn picking and device scaling; Windows also crosses the old terrain
resolution threshold without rebaking a warmed view.
