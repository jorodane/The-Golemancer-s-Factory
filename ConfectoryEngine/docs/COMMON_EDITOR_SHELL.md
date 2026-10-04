# Common editor shell migration

## Active task and resume instructions

The broader authorized goal is the complete common engine-pack UI migration.
The current authorized stage covers workspace/sidebar and participant management,
remaining Agent management, then common conversation execution including Linux.
The latest continuation checkpoint appears at the end of this document; earlier
checkpoint scopes are historical. Repository synchronization is an earlier completed request. On resuming after context
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

Current authorized stage: shared workspace/sidebar, participant roles/Join/name
updates and remaining Agent management, followed by shared conversation flows,
on top of verified main 1d918b7c11be61c4de16d7c931ae6311108b2b58. The initial
participant-name checkpoint below is partial progress. Continue this stage before
reporting it complete. Collaboration/YogiBox, packs/review and final Linux package
remain subsequent stages unless naturally required by these flows. Ordinary main
pushes are authorized.

All three editor hosts must mount the same installed engine pack elements and
invoke the same shared actions. Native adapters own drawing, input, measurement,
frame scheduling, file pickers and credential storage, not alternate workflows.
Project packs cannot replace the trusted shell or access credentials.

## Acceptance matrix

| Flow | Existing behavior to preserve | Migration status |
| --- | --- | --- |
| Startup | vector logo, staged entrance, logo flight to home, Connect/Later, saved Agent skip | pack presentation, trusted motion, saved-identity state and home branding shared; local verification passed |
| Agents | provider/auth/model/profile selection, restore, enabled state, private credentials | setup/provider policy and private profiles shared; restore/enabled/reconnect menus pending |
| Helpers | avatar/character/memories, Main Helper and worker roles | creation and MAIN selection shared within project creation; private profiles and global creation directory shared; workspace roles pending |
| Project home | recent cards, icons, rename/delete, last opened | shared cards/actions, branding and motion published; common Agent/Helper directory mounted; workspace sidebar surfaces pending |
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

## Agent connection checkpoint

Common Agent connection/model selection and private input were published in
61c747efc40e435f2d35e1398b16e08eb3a28035. Its new Windows native test used the
wrong namespace for EditorWindowState; e72b3eb247e8b56002e4ed20312a1335ee7978a6
corrected that reference. All five remote jobs passed in run 37187565091.
Latest exact published main: d0104dd1aab8c7de55f64f69ad69954a049be04f.
Action ownership checkpoint d0104dd passed all five remote CI jobs in run
37188287264.

The common workflow requires API transmission consent, safe indexed model
choices, separate CLI-install consent, staged provider ownership, encrypted
credential references, save rollback and disposal on cancellation/failure.
Windows uses existing DPAPI storage; Android uses its existing encrypted storage;
Linux supplies an async Secret Service adapter through secret-tool (key stdin only)
and an explicit-consent Codex/npm preparation adapter. Verification never uses
actual API/authentication, key-vault writes or CLI installations. Real Linux
Secret Service access remains unverified: this cloud has no secret-tool/session
vault. Private input cannot enter captured window state or clipboard; resets do
not manufacture user edits. No requests occur on mount or identity restore.

Checkpoint validation: engine isolation, Studio 225, pack 405, authoring 39 and
full explicitly selected external consumer campaign passed. Windows and Android
source/reference rebuilds, native Windows test compilation, actual SDL private
input/model/consent/connect/cancel/startup-flight checks and remote CI passed.
Android device execution is not represented by reference compilation or APK CI.

## Structural checkpoint review

The published d0104dd checkpoint moves implementation, not just source placement:
CoreTools supplies IEditorStudioActions from its verified installed DLL. Startup
saved-identity/home-entry policy, home, project creation, Agent connection and
provider creation/model/account policy compile into that engine pack. Host
facades invoke this factory and contain no alternate policy implementation.
Native adapters supply rendering/input, UI dispatch, OS credentials/pickers/CLI
preparation and explicit workspace options. The private shell loader only accepts
installed engine sources, validates deployment bytes before/after reading/loading,
and rejects project replacement of CoreTools even under a renamed DLL filename.
Its cached factory is still gated by deployment verification on every access.

Portable verification passed 410 checks (live view 127), including deployment
tamper/cached-factory and renamed DLL impersonation rejection. Engine isolation,
Studio 225, authoring 39 and the full external consumer campaign passed. Windows/
Android reference rebuild, Windows net48 CoreTools/native-test compilation and
actual SDL through the pack actions passed. Consent labels explicitly show
[동의함]/[미동의] without requiring unavailable checkbox font glyphs. Latest phase
screenshots stay outside Git under
/workspace/scratch/studio-pack-actions.png.{startup,home,agent}.png.

These successful flows do not establish full shell parity. Native Agent menus/
reconnect, workspace Helper roles, conversations, collaboration, pack/review
flows and the Linux toolbar/manual project surface remain active. Replace those
paths with the same trusted pack actions. Preserve the acceptance matrix above.
Physical source relocation or passing tests for one flow is insufficient evidence
that all native business workflows migrated. No final executable Library package
has been delivered for the complete migration because it is not complete.

Continue the current migration; do not return to the old repository-sync task.

## Profile / global Helper directory checkpoint handoff

This checkpoint implements common pack-owned name/avatar/character/private-memory/
experience actions and the global Helper creation directory. It is a bounded
checkpoint, not completion of the common shell migration. Implementation lives in
`editor/Packs/CoreTools/StudioProfile.cs`, `StudioDirectory.cs`, `StudioActions.cs`
and `ui.xml`; the private ABI is `editor/Confectory.EditorPacks/StudioShellActions.cs`.
Every host calls the verified installed CoreTools factory. Project overlays cannot
supply these private actions. No provider request is made by opening a profile,
listing identities, reading experience, creating a Helper or saving private data.

The pack validates names, owns explicit save/rollback, stages images until use or
cancel, stores new private assets under unique paths and preserves old image bytes
on removal. Avatar and character are independent. Invalid existing images show an
empty preview without hiding the profile/directory. The common limit is 12 MiB;
native adapters generate bounded PNG thumbnails. `editor.image` respects the
pack's 96/128/240 size and aspect ratio rather than drawing a tiny concept slot.
Private experience contents are read only after an explicit click; `editor.readonly`
supports selection/copy/scroll and is excluded from editable window snapshots.
Helper memory scope stays explicitly project-local or global across projects.

Native mounting/picker/thumbnail paths:
- Windows: `editor/Confectory.Editor/EditorWindow.ProjectHome.cs`, with profile
  aliases in `EditorWindow.Studio.cs`; native renderer in `EditorPackBackend.cs`.
- Android: `editor/Confectory.Editor.Android/MainActivity.ProjectHome.cs`, with
  alias in `MainActivity.Incidents.cs`; native renderer in `AndroidPackBackend.cs`.
  Superseded `MainActivity.HelperImages.cs` and request-code 3 handling are removed.
- Linux: `editor/Confectory.Editor.Linux/EditorSurface.cs` and `LinuxPackBackend.cs`.
  Temporary path pickers retain the common profile/directory and restore pack titles;
  scrolled content and hit testing stay within the profile/directory viewport.

Portable fixtures are in `tests/Confectory.EditorPacks.Verification/LiveViewVerification.cs`;
WPF renderer regression checks are in `tests/Confectory.Editor.Native.Verification/Program.cs`;
actual SDL pointer/text/keyboard fixtures are in
`editor/Confectory.Editor.Linux/EditorSurface.Verification.cs`. All service and
credential verification uses isolated fakes; no real authentication, paid inference,
credential registration or external CLI install is part of the tests. Screenshots,
logs and build outputs remain under `/workspace/scratch` or ignored build folders.

Structural limitations and next worker scope (not implemented in this turn):
- Windows `EditorWindow.ProjectHome.cs` / `EditorWindow.Studio.cs` still own worker
  participant-name projection, Join and workspace sidebar role selection. Android
  `MainActivity.ProjectHome.cs` / `MainActivity.Studio.cs` retain equivalent worker
  orchestration. Move these business paths with shared workspace/conversations.
- Linux Join is explicitly disabled because worker/conversation support is absent;
  its generic toolbar/manual project surface still lacks full common shell parity.
- Agent restore/enabled/disconnect/reconnect/model/account management still has
  active native menus in `EditorWindow.AiConnections.cs`, `MainActivity.AiConnections.cs`
  and the host project-home/sidebar files. Setup alone does not migrate all management.
- Workspace, send/stream/cancel/history/pointing, collaboration/Yogi/participants/
  inbox/handoffs/incidents and authoring/inheritance/global/import/export/plugins/
  granular review remain as described in the acceptance matrix. Keep scope separate.
- Specification paths: this acceptance matrix, `docs/PROJECT_EXECUTION.md`,
  `docs/UI_PACKS.md`, `docs/INHERITANCE.md`, `docs/TIMING.md`, `docs/CAMERA.md` and
  the repository/engine `AGENTS.md`. Read relevant contracts before each phase.
- Final executable delivery through Library awaits the complete shell; no incomplete
  package is delivered as completion by this checkpoint.

Local final validation: engine isolation passed; Studio 225, authoring 39 and
common editor packs 467 checks passed; the full selected external consumer clean
build/verification campaign passed. Forced Windows net48 and Android source/reference
builds, CoreTools net48 and native Windows test compilation passed with zero errors.
Actual Linux SDL window/input passed PROFILE GLOBAL_MEMORY IMAGE_REVIEW
READONLY_EXPERIENCE alongside startup, Agent setup, home and creation regressions.
Pixel checks verify staged preview rendering; screenshots were visually inspected
for full-size avatars and clean scrolled viewport/title restoration. This is actual
SDL execution in this cloud environment, not an Android device test or a claim that
the user's desktop package was executed. Windows native runtime is validated by
remote CI separately. No final Library executable is produced for this partial scope.

Implementation commit: `d926dea4d13adc3dd6d552ad0e313ef99ab19673` — Share
private profiles and global Helper directory through the trusted engine pack.
Ordinary push to main succeeded; exact remote SHA matched after publication.
CI run: [37192385241](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37192385241).
The documentation-only handoff commit follows this implementation; no new workspace
or conversation migration was started. Implementation exact-commit CI completed successfully: portable, Windows,
Android, Linux native and engine compatibility all succeeded in run 37192385241.
The Windows native workspace/worker interaction step succeeded; Android CI builds
are not device execution. This handoff changes documentation only and leaves the
implementation unchanged. The final documentation commit's CI is checked separately
before the turn closes; resolve its exact SHA with `git log -1 --format=%H -- docs/COMMON_EDITOR_SHELL.md`.

Resume from the clean published checkpoint, preserve private user state and migrate
only the next explicitly selected acceptance-matrix phase. No additional workspace/
conversation work was begun during this checkpoint. All superseded profile/name/
memory/image constructors covered by the new flows have been replaced; the native
workspace callbacks and menus listed above intentionally remain outstanding.

## Workspace-stage acceptance and participant-name checkpoint

The stage is complete only when the installed CoreTools factory owns the same
workspace and conversation elements/actions in Windows, Android and Linux.
Shared source outside the installed pack does not establish action ownership.

Required acceptance cases for the remaining stage:
- Workspace/sidebar: mount/reopen/project switch without provider requests;
  independent sessions; persisted panel/character placement; same role actions.
- Join/roles: new and repeated Join; enabled and disabled identities; missing
  Agent/Helper; MAIN selection; cancellation and reentry; persistence failures;
  owner/control boundaries and active-work guards; no private data in presence.
- Agent management: restore, select, enabled/disconnect/reconnect, model/account,
  cancellation/failure and resource disposal, using injected credentials/providers.
- Conversation: explicit send/stream/cancel, failure and retry, history/reopen,
  selected context and pointing; separate owner-private Helper/Worker exchanges;
  existing review/conflict callbacks and no automatic requests on opening a project.
- Execution: portable contracts on all hosts, Windows native CI, Android build
  distinct from device execution, and actual Linux SDL pointer/text/keyboard flows.
  Verify engine isolation, authoring and the explicitly selected consumer campaign.

The first implementation adds `IEditorStudioParticipants` to the trusted private
ABI. `StudioParticipants` is supplied by the verified installed CoreTools factory.
Windows/Android profile callbacks no longer implement participant-name policy;
Linux uses the same action for controlled participants already in session state.
Only AI participants controlled by the acting user receive the Helper's public
name. Another owner's matching Helper ID is not permission to update its character.
Mount is inert, repeated projection avoids redundant writes, and work authority is
checked on each action rather than cached. An IO persistence failure restores
in-memory participant names and propagates the error for explicit retry. This is
not a transaction across the independently saved private profile and presence.

Portable coverage tests installed-DLL ownership, another owner's matching ID,
unrelated workers, private-memory exclusion, idempotence, persisted reopen,
IO failure/retry and revoked authority on all three platform contracts. The actual
SDL profile fixture verifies Unicode name saving updates its controlled character
and preserves another owner's name. Runtime verification state and screenshots
are isolated under `/workspace/scratch`, outside Git. No paid inference,
authentication or real credential-store writes are part of verification.

Local checkpoint validation: engine isolation passed; common editor packs 494
checks (live views 211), Studio 225, authoring 39 and the full explicitly selected
external consumer clean build/verification campaign passed. Windows editor,
CoreTools net48 and native Windows test compilation passed with zero errors.
Actual Linux SDL input passed `PARTICIPANT_NAME` with the preceding profile,
private-memory, Agent setup, startup, project-home and creation checks. The profile
screenshot was visually inspected outside Git. Android device execution and a
local Android adapter build are unavailable in this environment (no Android
workload); portable Android contracts do not substitute for those checks. Remote
CI status must be independently confirmed after publishing this checkpoint.

Remaining scope is unchanged: pack workspace/sidebar definitions, Join and role
selection, Agent management and shared conversations are not implemented by this
initial checkpoint. At this initial name checkpoint Linux Join remained disabled; the continuation
below enables participant registration while conversation execution remains pending. No fallback shell or deleted feature establishes parity. Resume
from this checkpoint without duplicating the preceding profile/global Helper work.

## Workspace roles / Join continuation checkpoint

The installed CoreTools factory now supplies `IEditorStudioWorkspace` implemented
by `StudioWorkspace.cs` and the common `editor.studio.workspace` view in `ui.xml`.
Every host mounts that same role view. Windows and Android mount it in their
sidebars; Linux mounts it in its existing project page. Native menu MAIN actions,
Windows/Android profile Join and all hosts' saved-Helper restoration also call this trusted action factory.
The native callback attaches/renders a character or opens its existing conversation
surface after the pack commits participation. Mount does not start a provider.

The pack owns new/repeated Join, actor-local participant reuse, complete public
Agent/Helper identity publication, explicit MAIN Agent/Helper selection, empty
Main Agent selection, disposed-action guards and saved-role restoration. Missing
or disabled Agent connections cannot be silently reconnected by Join. Existing
joined characters can be opened during ongoing work; changing roles is guarded.
No private memories, credentials or model settings are exported to project roles
or presence. Manifest clashes retain the existing optimistic conflict detection.
Role persistence failure restores in-memory roles; failed presence publication
compensates already saved role/directory changes. Compensation failure is surfaced
explicitly. Native attachment failure is recoverable by rejoining the same saved
participant, without duplication. This is not a new collaboration lock system.

Portable cases cover all three platform contracts: inert installed-pack mounting,
directory-write failure, owner-specific Join versus another owner's matching ID,
complete identity and persistence, repeated Join during work, guarded mutations,
MAIN/clear controls, reopen, failed presence publication and compensation,
attachment failure/retry, manifest conflict, disabled Agent, private-state exclusion,
saved-role reentry and disposal. Actual SDL pointer input exercises role selection,
new/repeated Join and MAIN persistence. The existing home rename/delete-cancel
fixture scrolls to its actual target rather than assuming that target is last;
all original preservation assertions remain. Home drawing/hit bounds clip to the
content viewport so a scrolled role page cannot cover the fixed title/status areas.

Local verification: editor packs 551 checks (live views 268), engine isolation,
Studio 225, authoring 39 and the selected external consumer clean build/full
verification campaign passed. Actual Linux SDL passed `WORKSPACE_ROLES JOIN`
alongside previous startup, private Agent setup, profile/global memory, project
home/creation and semantic editing regressions. The final role screenshot was
visually inspected for clean fixed-title/content-viewport boundaries outside Git.
Windows editor/native-test net48 source compilation is recorded separately from
remote native execution; local Android build/device execution remains unavailable
without an Android workload/device. Verify the exact pushed commit's CI before
continuing publication claims. The preceding participant-name commit be9beb0's
five CI jobs passed in run 37193398164.

Published implementation: 8614a0b64e332e401cfc1689d56201690fbdd63c.
All five jobs (Windows native, Android build, Linux native/ARM64, portable/full
consumer campaign and engine compatibility) passed in
https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37194884189.
The final interaction review retains Windows' existing `JoinHelper(open: false)`
workspace navigation for Yogi-drop delivery. The common role-view participation
button itself registers without opening a conversation on every host. This host
navigation follow-up has its own ordinary push/CI verification; it changes no
provider calls, credential handling or collaboration response policy.

Structural remainder: the native directory/status/Yogi sidebar controls coexist
with the new role view. This does not yet establish workspace/sidebar parity.
Helper removal/role-picker toggles, worker promotion, character placement/window
persistence and the remaining sidebar definitions still need shared pack actions.
Windows/Android native conversation implementations remain active. Linux can join
and persist participants through the role controls, but its profile conversation
action remains disabled until real conversation/worker execution is implemented.
The role controls register participation without opening a conversation on any
host. Existing Windows/Android profile and icon conversation actions remain. Agent management and shared conversation flows remain the
same authorized stage. Continue them; do not treat this checkpoint as stage
completion or as final Linux shell/package delivery.

Next implementation handoff within this same authorized stage:
1. Consolidate the remaining native sidebar cards/status/actions into the shared
   pack view; preserve avatars/character images, unread/activity indication,
   explicit history/promote/open/hide actions, and Yogi drop targets. Migrate
   `DisconnectHelper`, `SyncProjectHelpers` removal and the `PickHelpers` toggles
   in Windows, plus Android equivalents. Keep actor-local filtering and active
   worker guards. Do not replace Yogi/collaboration policy with a new lock scheme.
2. Finish workspace character and movable-panel definitions/placement actions;
   native adapters retain primitive drawing, hit-testing, frame/input and OS
   services. Linux currently mounts role controls in the project page; its manual
   toolbar/project surface still needs the actual common workspace arrangement.
3. Finish Agent management from Windows `EditorWindow.AiConnections.cs`
   (`ConnectSelectedEditorAi`) and Android `MainActivity.AiConnections.cs`
   (`EditorAiMenu`, `ConnectEditorAi`), including the host sidebar disconnect
   paths. Reuse the installed pack's existing `StudioAgentService` provider
   ownership; do not start authentication, install CLIs or read credentials on
   mount/identity restoration. Verify cancel/dispose/failure with injected services.
4. Move conversation actions and UI from Windows `EditorWindow.Conversation.cs`,
   worker partials and `EditorWindow.Studio.cs`, plus Android worker/conversation
   partials. Retain `ConversationTimeline`, exchanges/history, explicit scoped
   context/pointing, streaming/cancel, reviewed local changes and clash callbacks.
   Implement actual Linux conversation/worker support from the same pack rather
   than exposing an inert conversation button. Profile conversation stays disabled
   there until this exists. Global significant Helper memory remains private to
   that Helper across projects; opening a project never starts paid inference.
5. Repeat meaningful platform/flow checks and exact-commit CI for the next code
   checkpoint. Full collaboration/Yogi, authoring/packs/review parity and final
   complete Linux executable delivery remain later parts of the overall migration.

## User-approved follow-on sequence (do not mix into current migration)

Complete the common shell acceptance matrix and executable delivery first.
Then implement, sequentially: foreman Task/Callback persistence foundation;
instant data tables; augmentation; render inheritance/authoring. The parent will
supply the complete follow-on specifications at their implementation stages.
Preserve each stage's specification, decisions, outstanding work, checks and
commits in repository documents for disconnect/context recovery.

Current Helper/workspace/collaboration constraints: Helpers have a global memory
space and can move between projects. Explicitly retain brief, meaningful larger
context. Unconfirmed changes remain local changes. Changes arriving at an active
work target use the existing conflict/collision Callback behavior. Do not add a
new scheme that locks another participant's work solely because an unconfirmed
change exists. The profile/global-memory work must preserve these semantics;
Task/Callback persistence or new data/render designs are not part of this turn.

## Participant lifecycle continuation checkpoint

This checkpoint advances the authorized workspace/participant stage after
608ebb34. It does **not** complete the workspace/sidebar/Agent/conversation scope.
The installed CoreTools factory owns Helper removal, role pruning, worker-to-Helper
promotion, character layout/move/commit and local display selection. Windows and
Android's live role-picker toggles, missing-profile removal, promotion callbacks,
character sizing/dragging and display controls now use those pack actions. Linux's
same mounted role view exposes real removal and rejoin; it still has no live
worker/conversation surface. Native callbacks retain character/window rendering,
input/density conversion, OS/private storage location and resource disposal.

Acceptance boundaries:

- Project removal detaches only controlled AI participants with the chosen
  HelperId, clears their views/presence/room membership and removes the project
  role. Another owner's matching Helper remains. Global enabled identity, memories,
  first experience, messages, drafts, work and review evidence survive. Standalone
  removal instead disables the private identity. A running target prevents all
  mutation; unrelated work is not locked by this action.
- Promotion requires an idle controlled ordinary AI worker and enabled source
  Agent, validates name/storage, preserves the supplied original conversation bytes
  in the new private Helper directory, then persists the directory/role/public
  identity. No original history enters project metadata or collaboration presence.
  Repeating promotion cannot create a duplicate. Native name dialogs still own
  their presentation; cancel invokes no promotion action.
- Persistence failures restore in-memory state and compensate already written
  files, including a Save observer throwing after publication. Compensation
  failures are explicit. Removal keeps original participant/view/member order on
  rollback. Native cleanup runs after successful persistence and cannot resurrect
  committed participation on failure. Join now also compensates saved presence
  when an observer fails after writing, preventing a phantom saved participant.
- Placement uses common logical coordinates, finite dimensions/positions, shared
  fit/clamp rules and viewer-local views. Arranging another owner's character never
  moves their public position. Read-only viewers can arrange/show/hide locally;
  this grants no work/control/name authority. Drag motion stays local until commit;
  failed commit preserves the local draft for explicit retry. Immediate-save move
  and display failure restore the previous local and persisted view. Windows/Android
  still preserve their existing cancel-gesture completion behavior.

Portable acceptance fixtures load the verified installed factory for each Windows,
Android and Linux contract. They cover running/owner guards, missing private
profiles, remove/rejoin/prune, global-memory/audit preservation, private-directory
and presence failures, post-write observer failures, native-cleanup failure,
promotion validation/duplicate/retry, exact private experience and standalone
identity disabling. Placement tests cover remote-owner/local-view separation,
read-only authority, scale/clamp, nonfinite dimensions, draft/commit/reopen,
failed persistence and explicit retry. Actual SDL pointer input additionally
removes/rejoins through the installed view and checks global memory preservation.
No paid AI request, external authentication or production credential write occurs.

Structural review / precise continuation:

1. Consolidate native sidebar directory/cards/status/menu elements into common pack
   definitions. Common role controls currently coexist with native management,
   Yogi/status and project menus. Participant list/worker settings, ordinary-worker
   creation/name/model/task/auto-confirm policies and movable-window preferences
   are still native. Promotion name-dialog and character/conversation definitions
   also remain to migrate; this checkpoint moves their lifecycle/layout policy.
2. Finish Agent management (enable/disconnect/reconnect, selected runtime disposal,
   account/model presentation) through the installed factory, reusing existing
   StudioAgentService and credential/service injection. Preserve source-provider
   support differences only as adapter capabilities. Never read credentials or
   restart requests on mount/reopen.
3. Continue common conversation execution/UI for all three hosts, including real
   Linux characters/send/stream/cancel/history/context/review behavior. Existing
   Windows/Android worker implementations still run. Preserve global significant
   Helper memory and semantic-locality/conflict callbacks; unconfirmed edits remain
   local, with no added locking. Linux profile conversation stays disabled until
   real execution exists. Do not replace its shell with a reduced fallback.
4. Subsequent collaboration/Yogi, authoring/packs/review and complete Linux package
   parity remain later stages. Supervisor/table/augment/render authoring remain
   outside this task.

Verification and publication evidence is recorded below after final checks.

Local final checks: engine isolation passed with no consumer, Studio 225 and
Authoring 39 passed, and the explicitly selected external Golemancer consumer
clean build/full verification campaign passed. Actual self-contained Linux SDL
passed startup/Agent/profile/global-memory/project-home/creation/semantic-editing
regressions plus removal/rejoin pointer assertions; the role screenshot was
visually inspected outside Git. Windows editor/native-test and installed CoreTools
net48 source compilation passed with zero warnings/errors. Android local
compilation/device execution remains unavailable without its workload/device;
remote Android build and Windows native execution must be confirmed against the
published exact commit. Live conversation, authentication, paid inference and
real credential-store writes are untested, intentionally replaced by injected
services where existing fixtures require provider behavior.

Final installed-pack suite: 701 checks, including 418 live-view/action checks.
All local required checks passed. Published source implementation:
94d45db83053fbcf125df6a7be6c7d98c03c7987, verified against the exact remote main.
All five CI jobs (Windows native, Android build, Linux native/ARM64, portable/full
consumer and engine compatibility) passed in
https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37197177425.
No generated assets or verification logs were committed.

This is a verified participant checkpoint, **not completion of the assigned
workspace/sidebar/remaining-Agent/shared-conversation stage**. The next worker
should resume from this implementation and the precise continuation above; do
not duplicate removal/promotion/placement, or revert to repository synchronization.
Remaining work is implementation scope, not a failed test or access blocker.
Android device behavior and local Windows native execution remain unavailable in
this Linux environment; remote build/native checks do not imply physical-device
conversation/gesture verification.

## Worker settings continuation checkpoint

Continuation after 48127794 replaces the duplicated Windows log name/model/task
form and ordinary-worker rename callbacks with installed CoreTools
`StudioWorkerSettings` and `editor.studio.worker-settings`. Android log/profile
settings mount the identical form, and Linux exposes it through the common
workspace participant settings entries. Windows/Android retain their existing
logs, promotion and proposal-authority actions; these were not deleted to simplify
the form. Native adapters present/close windows/pages and refresh runtime/UI state.
This is public participant configuration; changing a character's public name here
does not rename its separate private global Helper profile.

The pack owns draft name/model/public-task/auto-confirm state, explicit save,
current-owner/work-permission/active-request checks, normalization/validation,
persistence and compensation after failed/post-write-observer saves. Cancel and
reentry discard unpublished drafts. Changing auto-confirm does not grant proposal
scope or bypass review/conflict rules. Model lookup also belongs to installed
`StudioParticipants`: an explicit worker override wins, clearing it restores the
source Agent model, another owner's private Agent is inaccessible, and a disabled
source is never enabled implicitly. Windows and Android apply the resulting model
to their existing provider instances at send time. Mount/save cannot initiate
inference, authentication or credential-store writes.

Acceptance coverage loads the verified factory under all three platform
contracts: inert mount, unpublished edits, running/revoked-authority/owner-change
guards, complete explicit save, reopen/privacy, override/default/disabled-source
model selection, validation, write and observer failures, compensation, draft
retry, cancellation, reentry, remote-owner mount denial and disposal. Actual SDL
pointer/keyboard cases edit/save/reopen/cancel worker settings and verify that the
public name change leaves private Helper identity unchanged.

Remaining assigned scope continues: common sidebar cards/status/menu definitions,
ordinary-worker creation, promotion dialog/character/movable-window definitions,
Agent disable/reconnect/account/model management, and common conversation
execution/UI on all hosts, including real Linux send/stream/cancel/history/context
and reviewed local changes. Existing native conversations stay active. Existing
clash callbacks and semantic locality remain; no new locks, Supervisor/table/
augment/render-authoring features or reduced Linux fallback are introduced.
Verification/publication evidence follows when the checkpoint's checks finish.

Local checks: installed-pack suite 782, including 499 live-view/action checks;
actual self-contained Linux SDL settings save/reentry/cancel plus existing startup,
Agent, profile/global memory, home/creation, role/remove/rejoin and semantic-editing
regressions passed. The common settings screenshot was inspected outside Git.
Studio 225, Authoring 39, engine isolation, and the explicitly selected external
consumer clean build/full campaign passed. Windows/native-test and CoreTools
net48 source compilation passed with zero warnings/errors. Android local build/
device execution remains unavailable; exact pushed CI must confirm its build and
remote Windows/Linux native execution. Provider behavior remains fixture-backed;
no paid inference, real authentication or production credential writes occurred.

## Agent management acceptance contract (next checkpoint)

The installed factory must own the management view and explicit disconnect/
reconnect decisions. Disconnect checks active use before any write, disables the
private device-owned source, clears its selected identity, persists before runtime
cleanup, and preserves credentials, Helpers/global memories, participant metadata,
history and review evidence. Runtime cleanup receives only locally controlled
participants; another owner's matching AgentId is not authority to dispose it.
A private-source action must not create new project locks or mutate project roles.
Failed persistence restores the private source/selection and skips cleanup;
cleanup failure remains explicit after the committed disable and supports retry.
Reconnect opens the existing shared consent/setup form for the exact source; it
must not enable, authenticate, read credentials or make model calls by itself.
All hosts expose the same management elements and actions. Provider availability
comes from the installed AgentService and OS adapter capabilities. Mount, cancel
and reentry remain inert. Existing account/model/legacy Codex and conversation
features remain present until separately migrated, never deleted for this stage.

Worker settings published implementation:
68221d172bd3ab327445603c3791ef0bb9ad39a2. All five exact-commit CI jobs passed:
https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37199014887.

## Agent source-management continuation checkpoint

`StudioAgentManagement` and the common `editor.studio.agent-management` definition
are owned by the installed CoreTools factory. Windows and Android expose that
form from their Agent sidebar heading and shared workspace entry. Linux exposes
the same entry/form. Existing circular icons, private profiles, worker logs,
conversation actions, provider/account/model features and the old explicit Codex
connection workflow remain; they were not deleted to satisfy this checkpoint.
Windows/Android private profile disconnect, Android's old AI-menu disconnect and
Linux's new management controls use the installed action. Private profile setup
no longer selects/disposes the current runtime merely for opening reconfiguration.

Disconnect checks active use, saves the disabled private source and cleared
selected identity before native cleanup, and passes only owner-controlled AI
participants to that callback. Helpers/global memories, credentials, public
participation, histories and review evidence remain. Failed persistence restores
memory and compensates already written directory bytes. Runtime cleanup failure
is explicit after the committed disable; the form permits cleanup retry without
resurrecting or redundantly writing the source. Native adapters clear/dispose their
provider references and refresh windows, rather than owning disable/selection
policy. A source can be managed before a project is open; this private device
operation grants no project authority and requires no project work lock.

Reconnect validates active-use/provider capability and routes the exact identity
to existing shared consent/setup. It never enables the source, reads credentials,
queries models, installs a CLI or authenticates merely by being opened. The real
OS-capable AgentService builders are reused; no fake supported-provider fallback
was added. Unsupported sources remain inspectable/reconfigurable in profiles.
Owner-local effective-model inspection also now uses read authority, while worker
settings mutations/auto-confirm remain work-authorized. This preserves the
separation between inspecting private device configuration and editing a project.

Portable coverage includes inert mounting, profile/reconnect routing, active-use
denial, failed/private post-write persistence and compensation, selected-source
clearing, owner-filtered cleanup, global/private/audit preservation, repeated
cleanup, cleanup failure/retry, unsupported providers, cancellation, projectless
management and disposal. All provider calls are forbidden by the injected
management service. Actual SDL additionally opens the common management entry,
disables a fixture source while retaining Helper memory/public participation,
reconnects without activation/model/credential requests, and closes/reenters.
The common management screenshot was inspected outside Git.

Precise next handoff (the assigned stage is still incomplete):

1. Replace remaining native circular sidebar cards/status/popup/menu definitions
   in `EditorWindow.ProjectHome.cs`, `EditorWindow.CollaborationUi.cs`,
   `MainActivity.ProjectHome.cs`, `MainActivity.Presence.cs` and
   `MainActivity.Timeline.cs` with common pack definitions and primitive adapter
   gesture/render hooks. Preserve double-click/tap, Yogi drop and owner-specific
   public/private actions. Current common workspace/settings/management views
   coexist with native sidebar icons; this is not full sidebar parity.
2. Move ordinary-worker creation and promotion-name/character/movable-window
   definitions. Public worker name/model/task/auto-confirm settings and layout/
   display/removal/promotion persistence policy are already migrated; do not
   duplicate them. The separate private global Helper profile keeps its own name
   and concise significant cross-project memories.
3. Complete saved-Agent connection execution and live account/model presentation
   from Windows `ConnectSelectedEditorAi`/legacy Codex and Android
   `ConnectEditorAi`/AI menu via installed CoreTools. Shared setup and provider
   construction policy already exist; preserve legacy options/history/auth flows.
   This checkpoint completes source disable/reconnect routing, not every Agent
   execution/account/model-management gap. Use injected services for cancel,
   disposal/reentry/failure checks; no real credentials or paid requests.
4. Finish shared conversation execution/UI across all three hosts. Existing
   Windows/Android worker flows remain active; Linux still lacks actual worker/
   send/stream/cancel/history/context/review execution. Its conversation action
   stays disabled until real shared support exists. Preserve semantic locality,
   frozen scoped context, global/private Helper memory, reviewed local changes
   and existing incoming-change/clash callbacks; never add a lock system.
5. Collaboration/Yogi full parity, packs/review and final Linux package remain
   subsequent stages. Supervisor/table/augment/render authoring stay out of scope.

No user action or external-access blocker was identified. Continue this authorized
scope from the existing changes/checkpoints; do not restart or ask for approval.
Verification/publication evidence follows after final checks.

Final local management checks: installed packs 857 (live views/actions 574),
Studio 225, Authoring 39, engine isolation and the explicitly selected external
consumer clean build/full verification campaign passed. Actual self-contained
Linux SDL passed management disable/reconnect/close plus settings, role,
startup/Agent/profile/global-memory/home/creation and semantic-editing regressions.
Windows/native-test and CoreTools net48 source builds passed with zero warnings/
errors. Android local workload/device execution remains unavailable; remote build
and native jobs must be checked against the exact pushed commit. Authentication,
paid inference and production credential writes remain intentionally untested;
management provider calls are forbidden in its injected fixtures. This checkpoint
is ready for ordinary publication and exact-main CI verification.

Published Agent source-management implementation:
12fac59b42eeed264f81618fd910531a27ae951a, verified against exact remote main.
All five jobs (Windows native, Android build, Linux native/ARM64, portable/full
consumer and engine compatibility) passed in
https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37200782696.
The preceding worker-settings implementation 68221d172bd3ab327445603c3791ef0bb9ad39a2
also passed all five jobs in run 37199014887. Only source/spec/test changes were
published; no generated images, binaries, private data or verification logs entered
Git. The broader assigned workspace/sidebar/Agent/conversation stage is still
incomplete. Resume from the precise handoff above; these are verified progress
checkpoints, not all-shell parity or a request for another user approval.


## Ordinary Worker creation acceptance contract

The installed workspace factory owns ordinary Worker identity creation. An explicit
add action rechecks human work authority, idle state, the selected project Main
Agent (or the private selected source in the standalone editor), enabled source
and connection, and the host's provider capabilities. It publishes one complete
AI participant with owner, Agent, model and logical placement before attachment.
Mounting and creation never construct/connect a provider, query models, read/save
credentials or copy private Helper memory. Ordinary Workers have no Helper id.
Names and initial logical placement are identical pack policy on every platform;
viewport sizing/clamping and native conversation attachment are adapter duties.

Persistence failure removes the unpublished participant and compensates a
post-write failure. Attachment failure leaves the committed identity intact;
explicit reattachment must use that same id without registering another Worker.
Reattachment rechecks owner/work authority and does not rewrite identity or
restart providers. Existing worker conversations/history/review remain intact.
All native add entry points must call the installed action, and actual SDL must
exercise the shared add control and its settings without AI requests. This does
not claim completion of shared characters/sidebar or conversation execution.


## Ordinary Worker creation continuation checkpoint

`IEditorStudioWorkspace.CreateWorker` and `AttachWorker` execute from installed
CoreTools. Windows `AddWorker`, Android `CreateMobileWorker`'s ordinary path,
and the common workspace add control now use that policy. All three hosts supply
capabilities from their real installed AgentService; capability inspection neither
prepares a CLI nor constructs/connects a provider. Complete participant identity
is saved once before native attachment, including source model and owner. Windows
refreshes recipients before selecting the attached Worker. Android keeps its
existing conversation/character attachment and Helper branch. Linux can create
and inspect the public Worker settings; it still cannot execute conversations.
Existing Workers, providers, transcripts and private global Helpers are preserved.

The same explicit name and logical placement policy replaces Windows/Android's
different ordinary-creation defaults. Project creation uses Main Agent regardless
of another private selected source; standalone editor creation uses that private
selection and writes no project roles. Missing/disabled/unsupported source, idle
or work-authority denial happens before any publication. Persistence and observer
failures remove the new identity and compensate saved bytes. Failed native
attachment keeps the complete persisted identity; `AttachWorker` rechecks
owner/work authority and reattaches it without registration or provider restart.

Acceptance includes all three installed-pack contracts, shared add-control
activation, source-selection boundaries, standalone/empty-selection behavior,
private memory/credential-reference isolation, active/authority/capability guards,
pre-write and post-write failure/compensation, native attachment failure/retry and
disposal. Actual SDL creates an ordinary Worker from the shared add control,
checks its complete Agent/model/owner identity, opens common settings and closes
them. The created-Worker settings screenshot was inspected outside Git.

Checks: installed packs 932 (live views/actions 649), Studio 225, Authoring 39,
engine isolation and full explicitly selected consumer campaign passed. Actual
self-contained SDL passed creation plus prior startup/home/Agent/profile/Helper/
settings/management/role and semantic-editor regressions. Windows/native and
CoreTools net48 source builds passed with zero warnings/errors. Exact-main CI
is checked after ordinary push and its URL/hash reported in the final handoff.
No real authentication, paid inference or production credential writes were used.
Local Android workload/device execution remains unavailable, rather than failed.

Remaining handoff, in order:

1. Circular sidebar: add proper generic portrait/circle and gesture adapter
   contracts and common installed-pack sidebar/status/menu/profile definitions.
   Preserve the 112px paired circles, MAIN/selection rings, unread/activity,
   single/double click/tap, right/long click and existing Yogi drop. Native
   `AiCircle`/`MobileAiCircle`, `WorkerSidebarItem`/`MobileWorkerSidebarItem`,
   public/private profile popup definitions remain; no textual Linux replacement
   or gesture deletion is an acceptable completion.
2. Ordinary creation policy is now done. Promotion persistence, name/model/task/
   auto-confirm settings and layout/display policy were already done. Remaining
   promotion-name and shared character/movable-conversation definitions must use
   the existing installed actions, including `AttachWorker` for an existing id.
3. Saved-Agent execution/live account/model presentation remains in Windows
   `ConnectSelectedEditorAi`/legacy `ConnectCodexAsync` and Android
   `ConnectEditorAi`/AI menu. Reuse installed AgentService and shared setup;
   preserve legacy login, options, path preference, model and thread discovery.
   Source management/reconnect routing is done, not execution parity.
4. Shared conversations across all three hosts, including actual Linux worker
   send/stream/cancel/history/context/review. Preserve frozen locality, private
   global Helper memory, local proposals and existing conflict/clash callbacks.
   Continue using injected runtimes; no paid/auth/production credential checks.
5. Later collaboration/Yogi full parity, packs/review and final Linux package
   remain separate. No Supervisor/table/augment/render authoring expansion.

This is a verified creation checkpoint within an incomplete assigned migration.
There is no new approval requirement or external-access blocker. Confirm the
exact published main/CI using the final report and continue without restarting.
