# Common editor shell migration

## Active task and resume instructions

The active authorized task is the complete common engine-pack UI migration,
not the earlier repository synchronization request. On resuming after context
compression, read this document and inspect existing changes before proceeding.
Preserve unfinished changes. Do not report a synchronization result or a partial
checkpoint as completion of this task.

The user confirmed that all engine-pack elements remain identical and are
interpreted differently only by platform adapters, and authorized ordinary main
pushes. Preserve existing functionality, locality, consent and private state.
No force push, generated binaries, images, logs or secrets may enter Git.
External AI/authentication flows must use test doubles unless explicitly
authorized; do not transmit secrets or initiate paid requests for verification.

Completion requires every flow in the matrix below, platform rendering/input
adapters, regression checks and remote CI, actual Linux screen/input flows,
final exact remote main SHA, and an executable package delivered through Library.
Report source/reference compilation separately from desktop/device execution.

Current next checkpoint: common Agent connection/model UI and secure input. The recent
project checkpoint is published as f20337b404fdff3f2244eaaf5bae6b03a84c7f63;
all five remote CI jobs passed in run 37183768783. Startup motion/home branding is published as
e91cf44a4ac67bc72853c0956b43b8775b27d10c; all five remote CI jobs passed in
run 37184934309. Continue Agent connection/model/Helper management next, then
workspace, conversations, collaboration, pack management and granular review.
Do not mistake this checkpoint for completion of the overall task.

All three editor hosts must mount the same installed engine pack elements and
invoke the same shared actions. Native adapters own drawing, input, measurement,
frame scheduling, file pickers and credential storage, not alternate workflows.
Project packs cannot replace the trusted shell or access credentials.

## Acceptance matrix

| Flow | Existing behavior to preserve | Migration status |
| --- | --- | --- |
| Startup | vector logo, staged entrance, logo flight to home, Connect/Later, saved Agent skip | pack presentation, trusted motion, saved-identity state and home branding shared; local verification passed |
| Agents | provider/auth/model/profile selection, restore, enabled state, private credentials | pending |
| Helpers | avatar/character/memories, Main Helper and worker roles | creation and MAIN selection shared within project creation; profiles pending |
| Project home | recent cards, icons, rename/delete, last opened | shared cards/actions, branding and motion published; native directory surfaces still pending |
| Creation | name/icon/description/path, Main Agent/Helpers | shared controller and pack view mounted; portable and SDL input verified |
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

`6610c0a72eeddede3d6033cfaab024b6376705eb` publishes shared project creation
and fixes the startup wrapping/button-chrome regression. All five remote CI jobs
passed in run 37181292883, independently confirmed by the parent. This is partial
progress, not completion of the acceptance matrix.

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

The recent-project checkpoint mounts the same pack-authored two-column grid,
new-project tile and project card templates on every host. The common controller
owns draft/committed rename, catalog metadata, guarded OS callbacks and reversible
delete confirmation/cancellation. Obsolete native card workflows were removed.
Portable checks cover all three contracts; actual SDL text/Enter/pointer flows
cover inline rename and delete cancellation. Windows and Android source/reference
compilation passed; desktop/device execution remains a separate CI/device check.

The home checkpoint f20337b uses 358 pack checks, native SDL flows, source/reference
adapter compilation and the complete external consumer campaign. Its Windows,
Linux native, portable, Android and engine compatibility CI jobs all passed.

Startup motion/home brand checkpoint evidence: engine isolation, Studio 225,
authoring 39, full external consumer campaign, and pack 369/live-view 88 checks
passed. Windows and Android adapters were rebuilt without incremental outputs;
actual Linux SDL verified blank inert first frame, cubic entrance, keyboard
Connect/Later, three-element flight, input restoration and saved-Agent automatic
home entry. No external AI/auth request was performed. Phase screenshots are
kept outside Git under /workspace/scratch/studio-motion-final.png.{startup,home}.png.
Linux still has a legacy toolbar/manual project surface; Agent connection now mounts the common view/controller in all three hosts; directory/profile and legacy reconnect flows remain pending. Full shell parity and Library package delivery remain unfinished.

Resume next: implement the common Agent connection/model workflow and secure
write-only password renderer, preserving Codex install/login consent and encrypted
OS credential stores. The Agent controller/service, CoreTools view and write-only secret renderer are
now mounted in Windows, Android and Linux as uncommitted changes. Linux uses
Secret Service through secret-tool (stdin only) and an explicit-consent Codex/npm
OS preparation adapter. Do not run actual authentication, paid requests, key
writes or installations during verification. Latest portable fixture run passed 405 checks, including identity-save rollback
and asynchronous credential cancellation. Actual SDL Agent secret/model/consent/
connect/cancel and clipboard/window-state exclusion passed with injected services
and in-memory credentials. Fresh Windows/Android reference recompilation and actual SDL startup dialog
cancel/connect flight and private input verification passed. DLL-picker state
restoration was reviewed in source and remains to receive native input coverage. Android activity destruction now closes/cancels the shared setup dialog.
The /tmp drafts are stale; use repository source. Publish only after latest
fixtures, native checks and the full required campaign pass. Remaining Agent
profiles/Helper management, workspace, conversations, collaboration and pack/
review flows are still pending.
 Prefer model-choice
command indexes over remote model IDs. Stage providers until identity/credential
persistence succeeds; dispose unadopted providers on failure/cancellation.

Latest exact published main: e91cf44a4ac67bc72853c0956b43b8775b27d10c.
Its Windows/Linux/portable/Android/engine compatibility jobs passed. The current
Agent work must preserve that baseline and the full remaining acceptance matrix.
The uncommitted connection controller stages providers, requires API consent,
uses indexed model commands, persists only encrypted credential references,
rolls back failed identity saves and disposes unadopted providers. Real external
AI/authentication has not been used. No final executable Library package has
been delivered for the complete migration because it is not complete.

## Structural checkpoint review

CoreTools owns the common Agent connection elements and bindings. The shared
EditorPacks controller owns consent, provider/model choices, staged connection,
credential reference publication and save rollback. EditorStudioAgentService owns
API/custom provider creation and model/account probe policy; native hosts supply
only OS CLI preparation, credentials, UI dispatch and workspace options for this
flow. Superseded native setup constructors and dead credential-save helpers were
removed. Linux startup cancellation preserves its existing entrance and selected
DLL browsing preserves the controller and form state.

This does not establish full structural migration. Shared controllers currently
execute from the engine EditorPacks assembly, not a pack-module action factory.
Legacy native Agent menus/reconnect, profile/Helper, conversation, collaboration,
pack management and Linux toolbar workflows remain active. Physical source
relocation alone is insufficient: these paths must be replaced by the same trusted
pack actions before the complete shell is accepted. Passing behavioral checks for
one flow is not evidence that the remaining native business workflows migrated.

Agent connection checkpoint validation: engine isolation, Studio 225, pack 405,
authoring 39 and the complete explicitly selected external consumer campaign
passed. Windows/Android reference rebuild and actual SDL smoke passed. No real
AI/key-vault/authentication request or CLI installation was used. Remote CI has
not yet been run for this uncommitted checkpoint.
