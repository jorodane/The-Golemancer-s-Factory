# Common editor shell migration

All three editor hosts must mount the same installed engine pack elements and
invoke the same shared actions. Native adapters own drawing, input, measurement,
frame scheduling, file pickers and credential storage, not alternate workflows.
Project packs cannot replace the trusted shell or access credentials.

## Acceptance matrix

| Flow | Existing behavior to preserve | Migration status |
| --- | --- | --- |
| Startup | vector logo, staged entrance, logo flight to home, Connect/Later, saved Agent skip | shared pack presentation mounted; native motion/state migration pending |
| Agents | provider/auth/model/profile selection, restore, enabled state, private credentials | pending |
| Helpers | avatar/character/memories, Main Helper and worker roles | creation and MAIN selection shared within project creation; profiles pending |
| Project home | recent cards, icons, rename/delete, last opened | pending |
| Creation | name/icon/description/path, Main Agent/Helpers | shared controller and pack view mounted; visual/flow verification in progress |
| Workspace | independent project surface, role characters, movable panels and persisted layout | pending |
| Conversation | send/stream/cancel/history/context, reviewed changes, no automatic requests on open | pending |
| Collaboration | Agent–Worker–Helper, YogiBox, participants/inbox/control/handoffs | pending |
| Packs | create/inherit/implementation/enable, plugins/global packs/import/export | pending |
| Review | individual selection, conflicts, consent and histories | pending |

## Implementation order

1. Add a trusted, declarative shell view to the existing installed CoreTools pack;
   mount it before project execution in Windows, Android and Linux. Extend native
   renderer contracts for pack-owned vector geometry and presentation.
2. Move device directory and project catalog state/actions into shared controllers;
   mount pack-owned Agent, Helper, home and creation views in all hosts.
3. Move conversation, collaboration, authoring, review and window state into
   shared controllers, keeping project queries owner-local and cross-pack queries
   explicit. Remove superseded native screen constructors only after regression
   coverage demonstrates their replacement.
4. Check engine isolation and the selected consumer campaign at each published
   checkpoint. Final verification includes remote CI and actual desktop/device
   flows, with source compilation reported separately from native execution.

This document tracks work in progress. A startup checkpoint is not full parity.

## Checkpoint evidence

`6774d0da7227594092a9d6c7ce11a59281347491` mounts the common startup
presentation on all three hosts. Local native SDL and portable verification passed.
Its remote Windows startup check caught wrapping/button-chrome regressions;
`wrapText`, font weight and pack-authored button states address that regression in
this next checkpoint without weakening the original native assertion.

Project creation is now driven by `EditorStudioProjectCreation` and the installed
`editor.studio.new-project` view. Native hosts supply file/folder pickers, UI thread
scheduling and activation callbacks. Agent/Helper choice, Helper creation, MAIN
selection, validation and metadata writes are shared. Portable tests exercise all
three platform contracts, preserve entered values through role changes, reject
relative paths and existing output, and exclude private Agent state from manifests.
The actual SDL smoke creates a Korean-named project through pointer/text input.
