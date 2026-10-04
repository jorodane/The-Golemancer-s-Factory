# Linux native editor

The Linux editor uses an actual SDL2 window and Skia texture renderer. It mounts
`editor-1` UI contracts and executes independently loaded editor-pack DLLs. The
same engine-owned `Confectory.Platform.Sdl` window, input, controller-device and
render loop hosts Golemancer; its `GameSurface` adapter retains all game meaning.
The engine never selects a bundled consumer.

## Build and start

```sh
CONFECTORY_DOTNET=/path/to/dotnet ./BuildEditorLinux.sh linux-x64
./StartEditorLinux.sh --project /absolute/path/Project.packproject
# On ARM64, build with linux-arm64 instead.
```

Both distributions include the .NET runtime, Skia native binaries and a verified
CoreTools pack engine. The desktop needs SDL2 (`libSDL2-2.0.so.0`), fontconfig and
fonts; Noto Sans CJK provides Korean text. No browser or .NET SDK is needed to
open/edit projects. Build/run/verify actions use the selected project's commands;
building additionally requires a .NET SDK and the matching engine source root:

```sh
./start.sh --project /path/Project.packproject \
  --engine-root /path/ConfectoryEngine --dotnet /path/to/dotnet
```

Without `--project` the application opens a generic project picker. Opening a
project does not load project DLLs or run commands. “Load / run editor packs” is
an explicit execution action. Pack windows and menu navigation use the installed
CoreTools and the selected project's EditorPacks directory. Runtime preparation
and native view construction complete before replacing the previous generation.

Concept maps, hover/Shift references, schema fields, composite/repeated values,
reference/function choices, pack ownership/moves, functions and implementation
source use the shared `ConceptEditorController`. Object views support table,
card, slot and registered ObjectEditor layouts. Native controls handle text/IME,
clipboard, selection, keyboard focus, pointer activation and scrolling. Reviewed
DLL changes show before/after content and require Apply or Reject. Hash checks,
read-only rules and stable identities remain in the common engine services.

## Reproducible native verification

```sh
python3 tools/verify-editor-linux.py --dotnet /path/to/dotnet \
  --screenshot TestResults/native/linux-editor.png
```

`--smoke` always creates an isolated temporary project, even when `--project` is
also supplied. It sends events through SDL's native queue, edits/saves a schema,
loads the real CoreTools DLL, executes a command, and mounts a returned dynamic
view. The screenshot comes from the application's actual submitted bitmap.
Without DISPLAY or WAYLAND_DISPLAY the verifier uses SDL's offscreen driver.
This verifies the native window/renderer/texture path, not desktop compositor
integration, hardware IME, physical controllers or an ARM64 CPU. Those require
execution on the corresponding desktop/device. ARM64 publishing is a separate
build check. Golemancer's `--smoke` also exercises native keyboard/pointer events
and an independent engine UI button DLL through the shared SDL host.

Native binaries, screenshots and runtime packages are generated outputs and are
not committed. `BuildEditorLinux.sh` writes under `editor/Builds/Linux/<rid>`.
