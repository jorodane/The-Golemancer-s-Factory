# Common editor shell migration

> Active direction (user-confirmed 2026-10-04): follow
> [HELPER_SUPERVISION.md](HELPER_SUPERVISION.md). Helper-facing supervision replaces
> earlier Worker-direct-chat/promotion plans below. Older checkpoint descriptions
> are historical evidence, not authorization to continue the superseded UI.


> Latest accepted UI/context specification: [FINAL_UI_CONTEXT.md](FINAL_UI_CONTEXT.md),
> read in full from the user's finalized 798-line document. Apply only remaining
> deltas. It supersedes the historical review-only layout/YogiBox cautions below.
> Helper chat must work without a project and must never auto-open one. Esc and
> whole-box × discard only the current YogiBox draft; delivered history remains.


## Current resume checkpoint

Latest CI-complete, exact remotely verified implementation: `cd0ee2ae1c5df59aba96fecbc085e9c42d1fcbe8`.
All five jobs passed in [CI 37267513899](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37267513899),
including 98 actual Windows native checks, 2,408 portable checks, actual Android
APK build and actual SDL regression. Installed public controller, native bindings,
common transcript viewport and navigation definition/action foundation are published.
Native final lower-panel/navigation adoption remains unfinished.
The viewport implementation `534e5fd30c1166c7493637feca0a80b6d405b558` passed
[CI 37265345014](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37265345014).
The preceding public-lifetime correction `efddefe5d7a76764afdba8f57d8405304de7bd25`
passed [CI 37264710834](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37264710834).
The preceding foundation `1e907cb5bce816789653446f3926950e6b99f25e` passed
[CI 37261206655](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37261206655).

Previous recovery checkpoint: `5580f95fabe7db19ca194bebb89c37f43944cd96`.
All five jobs passed in [CI 37258012412](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37258012412),
including 88 actual Windows native checks, 2,189 portable checks, actual Android
APK build and the SDL legacy-recovery regression.

Previous Linux adoption checkpoint: `cbed5eda4cf0fac1adda672ad67049aabdae3d21`.
All five jobs passed in [CI 37255043663](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37255043663),
including 85 actual Windows native checks, 2,117 portable pack checks and the
Linux production regression. The preceding Yogi checkpoint is recorded below.
All five jobs passed in [CI 37251133809](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37251133809),
including **85 actual Windows native checks** and **2,075 portable pack checks**.
Temporary pack-owned Yogi composition/inspection is published on Windows/Android;
Linux production adoption is the active local continuation.
Windows/Android production Helper conversations and shared selective review are
published. This revision adopts Linux production Helper conversations, temporary
Yogi and installed conflict choice; completed local checks and next scope are at
the end of this document. All five remote CI jobs are confirmed successful for this increment. Workspace roles/Join/names, participant
lifecycle/settings, Agent management, portraits/sidebar and saved-source actions
are installed CoreTools checkpoints. The assigned stage remains incomplete.

Resume at the latest continuation at the end of this document. Preserve temporary
Yogi work and all recovery stashes. Do not apply the superseded promotion stash or
repeat completed migrations. [FINAL_UI_CONTEXT.md](FINAL_UI_CONTEXT.md) and
[HELPER_SUPERVISION.md](HELPER_SUPERVISION.md) govern the remaining Helper-centric
flows; later supervisor persistence, instant-table, augment and render-authoring
stages remain sequenced separately.

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


## Common circular portrait acceptance contract

Circular Agent/Helper/Worker portraits are installed-pack elements, not separate
native shell drawings. CoreTools owns the Unicode glyph, avatar/glyph choice,
MAIN badge, selection/status/MAIN rings, unread dot, shared logical size and
activity/color policy. A generic `editor.portrait` primitive interprets explicit
image/glyph/rim/inner-rim/badge/indicator/diameter/dash properties in Windows,
Android and SDL. Renderers know no Agent, Helper, participant or AI state.
Private image decoding/thumbnail bytes are an OS boundary; the primitive accepts
only bounded bitmap data URLs, never a local path or remote fetch.

Existing native circles must mount the installed definition and route activation
through its factory. Keep all current single/double click/tap, right/long click,
profile settings and Yogi drop hooks; replacing circles with text or losing
gestures is not parity. Worker ring/activity calculations use the installed
portrait policy and no native shape overlays. Mount/update/dispose never create
a provider or read credentials/global Helper memory. Detached views must release
images and commands. Verify shared definitions/policy, Unicode/fallback/empty/
MAIN/selection/activity/unread states and actual SDL rendering/input. Circular
primitive migration alone does not complete surrounding sidebar menus/popovers,
movable Worker presentation or conversations.


## Circular portrait continuation checkpoint

Installed CoreTools now defines `editor.studio.portrait` and owns
`IEditorStudioActions.Portrait`. The generic `editor.portrait` renderer contract
is implemented by WPF, Android and SDL with explicit symbol, image, rim/inner
rim, badge, indicator, dash and diameter properties. Renderers contain no Agent/
Helper/participant or AI-state policy. Shared pack policy determines status,
colors, first Unicode symbol, MAIN/selection, unread state and logical geometry.
Unicode handling preserves combining marks, joined emoji/modifiers/flags and
decomposed Korean syllables consistently instead of differing StringInfo runtime
behavior between net48 and modern .NET.

Windows `AiCircle` and Android `MobileAiCircle` now mount the installed definition,
including existing sidebar, role-picker and profile portrait usages. Worker
sidebar status/MAIN/unread overlays no longer construct native shapes; they use
the same pack portrait policy. Native adapter helpers only supply bounded private
image thumbnails, host attachment and existing click/profile/gesture/drop hooks.
Windows unload releases commands/images; Android detach queues release until
a native RemoveAllViews traversal completes. Android's Worker MAIN menu also
uses the existing installed workspace role action rather than a direct role write.

Common workspace Agent/Helper role controls use that same circle data/definition
with a two-column 96px Agent group, selected rings, MAIN Helpers and private
thumbnail callbacks on all three hosts. Linux's actual application role controls
therefore use the new primitive, in addition to direct factory verification.
No source setup, provider/account/login, conversation/history, Helper memory,
startup motion, role, Yogi drop or native profile action was deleted.

Portable checks cover installed ownership/inert mounting, explicit activation/
disposal, Unicode symbols, avatar fallback, empty/dashed/selection/MAIN states,
all existing result-state colors/activity, unread rings/dots and bounded image/
geometry/symbol/color contracts. Actual SDL exercises role selection/Join through
circles plus three real factory portraits: MAIN/status/unread Unicode, dashed add
and an injected real bitmap avatar with selection and clipping. Their screenshots
were inspected outside Git. Windows native assertions inspect the new actual
DrawingImage glyph/ellipse/dash/unread geometry, retaining the prior visible
empty-slot and unread behavior checks rather than dropping them.

Local installed packs: 1004 checks (live views/actions 721). Actual SDL and engine
isolation passed. Studio 225, Authoring 39 and the full explicitly selected
consumer campaign passed. Windows/native and CoreTools net48 source builds
passed with zero warnings/errors. Exact-main CI is checked after ordinary push
and reported with its hash/URL in the final handoff. No authentication, paid
inference or production credential writes were used. Local Android device/workload execution remains unavailable.

Precise remaining handoff (assigned migration still incomplete):

1. Circular primitives and their native usages are now migrated; do not recreate
   native circle drawings. Remaining **sidebar composition, captions, menus and
   profile popovers** in Windows ProjectHome/CollaborationUi and Android
   ProjectHome/Presence/Timeline still need installed definitions/actions.
   Preserve paired 112px management, existing gestures, private vs public owner
   actions and Yogi drop. Common role forms still coexist with native sidebar
   composition; this is not complete sidebar parity.
2. Shared Worker characters, promotion-name dialogs and movable conversation
   presentation remain. Ordinary creation/reattachment, settings, promotion
   persistence and placement/display policy are already done. Reuse them.
3. Saved-Agent execution and live account/model presentation still use Windows
   ConnectSelectedEditorAi/legacy ConnectCodexAsync and Android ConnectEditorAi/
   AI menu. Preserve login, path preferences, models, options and thread/history
   discovery while moving execution into installed CoreTools.
4. Shared conversations, including actual Linux send/stream/cancel/history/
   frozen context/review, still remain. Keep semantic locality, global private
   Helper memory, local unconfirmed proposals and existing clash callbacks.
5. Later collaboration/Yogi full parity, packs/review and final Linux package
   remain separate; no Supervisor/table/augment/render-authoring expansion.

No new user approval or access blocker was identified. Continue from these
verified changes and remaining owner paths, not from an older pull-only task.


## Shared sidebar composition acceptance contract

Installed CoreTools owns the 96px content/two-circle columns inside each native
112px management region, Agent/Helper/Worker/human ordering, captions/status,
profile/context panes and their action availability. Hosts supply current Worker
activity facts, thumbnail decoding, popup anchoring and OS gesture/drop events.
They must not retain native sidebar source loops or profile/menu definitions.
Single activation opens the common pane without acknowledging answers. Double
activation opens/joins the exact Worker, context activation opens the same common
actions, and Yogi drop retains existing native input routing. Only controlled
participants can expose private Helper assets/settings/history. Foreign Workers
keep public viewing/call and viewer-local hide actions.

MAIN, Join, Helper/source disable and ordinary disconnect recheck installed
authority/lifecycle policies. Failed persistence skips runtime cleanup and
compensates saved state; cleanup retry must not restart a provider. Preserve
all prior settings, logs, promotion, open/hide, public call, YogiBox and human
inbox/drop capabilities. Unavailable Linux conversation/runtime capabilities are
explicit; no fake answers or simplified replacement shell. Actual SDL must mount
the same composition/panes and verify passive mount, gestures/actions, owner
boundaries, cancel/reentry/failure, with injected runtime services.

## Shared sidebar continuation checkpoint

This continuation starts from verified main
`513f60ae34f05f632342cf55142298106c5a1a2c` and supersedes the sidebar
composition/menu bullet in its handoff, not the entire remaining shell migration.

- Installed `CoreTools.StudioSidebar` owns ordered Agent/Helper/Worker/human
  composition, two-column circle groups, compact Worker name/status captions,
  MAIN/unread presentation, and profile/context menu definitions and actions.
  Windows and Android remove their native sidebar loops and profile menu bodies;
  Linux mounts the same factory beside its existing project surfaces.
- Adapters supply activity facts, bounded thumbnail decoding, popup/scroll
  placement, and native context/double-activation/drop input. Common menu actions
  retain private settings, histories, promotion, open/hide, public call, source
  management, role selection, YogiBox and human inbox routes. Ordinary creation
  and Worker visibility also remain available in standalone mode.
- Foreign Workers use public names/status and cannot read matching local Helper
  avatar/character paths. Opening a menu does not acknowledge answers. Mutations
  recheck current authority; ordinary disconnect saves hidden/disconnected
  presence before runtime cleanup, compensates failed persistence (including new
  presence/view entries), and leaves a failed cleanup safely disconnected for
  retry. Failed native pane mounting permits reentry.
- The generic native text contract adds `overflow=clip|ellipsis`; compact common
  captions select ellipsis and carry their full-name tooltip. SDL hit-testing
  follows actual paint order with child controls above their containers. Menu
  scrolling remains an OS adapter boundary.

Verification evidence and publication status are recorded below after the final
checks. No paid inference, external authentication, or production credential
writes are authorized by these checks. Generated screenshots/logs remain outside
Git, and `.gitignore` is unchanged.

### Next handoff after sidebar

1. Migrate Worker character/bubble presentation, promotion-name input and movable
   conversation controls into installed pack definitions/actions. Existing native
   attachment and real AI/Helper/Worker interactions remain in place meanwhile.
2. Migrate saved-Agent execution and full shared conversation flows. Linux has no
   attached Worker conversation runtime yet: common open/log/promotion controls
   explicitly reflect unavailable capability; its current YogiBox/human inbox
   attachment routes also remain unavailable. This is an incomplete migration,
   not a Linux replacement shell or a claim of full platform parity.
3. Preserve shared global Helper memory, request-local semantic context and the
   existing incoming-change clash callbacks. Collaboration/YogiBox completion,
   packs/review and final Linux shell/package remain subsequent stages. Do not
   introduce Supervisor, instant-table, augment or render-authoring features.

Android device interaction and Windows GUI interaction require their respective
native environments; portable adapter tests, local Windows source compilation,
actual SDL execution and exact-head CI results must be distinguished explicitly.

### Sidebar checks completed locally

- Installed engine-pack campaign: **1,073 checks**, including the same sidebar
  factory on Windows/Android/Linux test backends, standalone creation/visibility,
  privacy, stale authority, active-worker rejection, persistence compensation,
  cleanup failure/retry, native mount failure, unavailable execution and disposal.
- Actual SDL: `LINUX_EDITOR_SMOKE_PASS`, now reporting `SHARED_SIDEBAR`; native
  context click, scrolling, close/reentry, private Helper profile and ordinary
  Worker settings pass alongside existing startup motion, private connection
  fixtures, profile, Join/MAIN, image review, project creation/home and pack DLL
  checks. The screenshot was visually inspected. Linux real AI conversations
  remain unavailable rather than tested successfully.
- Studio **225**, authoring **39**, engine isolation and the full explicit
  `ConfectoryProjects/Golemancer/Golemancer.packproject` consumer campaign pass.
- Windows native-verification source and installed CoreTools compile on **net48**
  with **zero warnings/errors**. Local Windows GUI and Android device execution
  are unavailable; remote Windows GUI/Android build CI is checked after push.
- Evidence: `/workspace/scratch/sidebar-{packs,linux,studio,authoring,isolation,
  consumer,windows-source,core-net48}.log`; screenshots under
  `/workspace/scratch/sidebar-native.png.*`. These are not committed artifacts.

### Sidebar initial-home CI correction

The first publication `bdfcbeb0e508babd65e5c0ce29597defeaa80655` passed local
checks and remote Android/portable/Linux jobs, but Windows GUI CI run
[37209471978](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37209471978)
failed its existing initial Agent/Helper circle assertion. Native compilation
passed; the adapter had incorrectly omitted the sidebar when the initial home
had no project session. This was a real lifecycle regression, not an unavailable
Windows test, and the assertion remains intact.

The correction lets the same installed sidebar accept an absent collaboration
context for the global home. It mounts identical global identity groups and
private profile settings without creating a workspace, reading project activity,
starting a provider or writing project presence. Project role controls are omitted
and Worker creation/Helper workspace actions are disabled until a workspace exists. Windows,
Android and Linux use this common lifecycle contract. Added portable sessionless
coverage brings the installed-pack count to **1,085**. Actual SDL passes again;
Windows source/CoreTools net48 builds again have zero warnings/errors. Exact-head
CI confirmation follows below.

### Sidebar verified publication

Code correction **`aa439dca12a469534a0e762daa063ae5bc020ca9`** is confirmed at
remote `refs/heads/main`; the tree was clean after ordinary pushes.
[CI 37210355823](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37210355823)
completed successfully for **all five jobs**: Windows, Android, Linux native,
portable and engine compatibility. Windows reports **34 actual native GUI
interaction checks**, including the unchanged initial sidebar assertion,
independent Worker conversations, read receipts/unread dots, private input and
native profile/image behavior. Android native compilation passes; physical device
interaction remains untested. Linux runs the actual SDL window; real Worker AI
conversation execution is still unimplemented rather than passed or faked.

This is the next coherent sidebar checkpoint only. Continue from the numbered
handoff above; do not repeat profile/Helper, worker settings/creation, management,
portrait or completed sidebar work. No current SDK/CI blocker remains. The larger
workspace/participant/conversation task remains open until Worker presentation,
saved-Agent execution and full common conversation flows are implemented and
verified across the required platforms.

### Cold native layout follow-up

The documentation-only head `c5f7c9f2154bd16c9889338bd9db0ee1dd04e952`
exposed a Windows timing failure in
[CI 37210710294](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37210710294):
a slow initial home layout consumed the motion interval before its first frame.
The prior code checkpoint's five passing jobs remain valid evidence, but this
later Windows failure must also be tracked. The Windows adapter now starts its
motion clock after layout, preserving the pack's duration/easing and three brand
elements. Native verification deliberately delays that layout by 800 ms and
retains the existing motion, reentry and resize assertions. Local native net48
source build passes with zero warnings/errors; actual GUI confirmation is in the
new commit's CI. Engine isolation, full explicit consumer, Studio, authoring and
pack checks from the sidebar campaign remain recorded above.

### Verified sidebar state and superseded promotion work

Cold-layout correction `9e6563751a04aa27837bbd4ba1335712cc209fd6` passed all
five jobs in [CI 37211025341](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37211025341),
including the deliberate slow-layout Windows GUI check.

The user has confirmed Helper-facing supervision; the prior pending question is
resolved. Worker-direct-chat and Worker-to-Helper promotion are superseded as
specified in [HELPER_SUPERVISION.md](HELPER_SUPERVISION.md). Existing published
records and behavior must be preserved until tested replacement routes land.
The unpublished common promotion form/adapters/tests remain archived in local
stash **`3408b1e2c61752975aca66810dbc9d468d44aee4`**, labelled `Pending design
decision: shared promotion form, local checks 1157 and SDL passed`. That label is
historical; do not apply the superseded feature. Evidence remains in
`/workspace/scratch/promotion-packs.log` and `promotion-linux.log`. The stash is
local preservation, not published functionality. Continue the explicitly
sequenced provider, migration, Helper-shell and supervisor-persistence work.

## Saved-Agent execution acceptance contract

A verified installed factory owns saved-source selection validation, connection
snapshots, credential-slot reads, platform support, connection concurrency,
cancellation, stale-source/context rejection and candidate lifetime. Constructing
it and restoring a saved identity remain inert. Explicit connection reuses the
exact enabled source and credential reference; it never writes credentials or
falls back to another source. Missing execution prerequisites route to the common
setup/consent flow. Successful adoption transfers one connected candidate;
failed/cancelled/stale attempts dispose it and preserve the incumbent connection.
Native services retain OS preparation, credential vault access, UI dispatch and
attachment/rendering. Existing Codex account/login notices, model choices and
private conversation restoration must survive migration. Verify with injected
services, including late completion after cancel/dispose and source/context
changes during a pending connection. This runtime checkpoint does not expose direct Worker chat or promotion.

### Saved-source runtime checkpoint and confirmed next direction

The confirmed supervision specification is published as
`a45a89b1dd62cc89119192e628e249209d82d89a`; all five jobs passed in
[CI 37213271759](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37213271759).
Follow `HELPER_SUPERVISION.md` before continuing conversation work. The promotion
stash above is superseded and must not be applied.

`CoreTools/StudioSavedAgent.cs` implements the installed factory's explicit saved
connection action. It validates exact source/credential slot, enabled/support and
workspace facts, snapshots provider settings, gates overlapping attempts, rejects
changes during vault/provider waits, and transfers only a current successful
candidate. Failed adoption/cancel/disposal retains the incumbent. Even throwing
candidate cleanup releases the action for reentry. Credential stores are read
only. Resident reuse is an explicit host fact checked after pack authorization;
missing runtime preparation returns the exact source to shared setup/consent.
`StudioAgentService` captures private request options before asynchronous CLI
preparation. Native hosts supply OS preparation, vaults, UI dispatch and provider
attachment; they do not create saved provider candidates themselves.

Windows uses this action for API/custom/Codex saved reconnect and preserves
account/model selection, CLI login/Node guidance, path preference and private
history restoration. Its new injected native GUI fixture covers initial adoption,
late cancellation/incumbent preservation and retry without credential writes.
Android uses the same action for saved API sources and releases a provider when
changing projects so its connection cannot retain the old project's options.
Linux has the same explicit saved connection boundary with actual SDL completion,
cancel and retry fixtures; Helper request routing into this boundary is still a
subsequent implementation, not a completed Linux conversation feature.

Local acceptance: installed-pack verification **1,265 checks**; independent engine
verification passes without a consumer; Windows net48 native source/test build
passes with zero warnings/errors. Actual SDL saved-source adoption/cancellation/
retry and the full explicit consumer campaign pass; Studio reports 225 checks.
Remote CI for this code checkpoint is pending publication. Logs and
screenshots remain outside Git under `/workspace/scratch/saved-agent-*`. No paid
inference, external authentication or production credential writes were performed.
Physical Android interaction and live provider account/history integration remain
unverified; fixture results do not imply those tests passed.

Next coherent work is explicit supervisor assignment and recoverable record
migration, then common Helper-facing conversation/character/request actions and
workload/object indicators. Preserve promoted Helper identities and global memory;
do not infer a Worker supervisor from `OwnerId`, Agent source, MAIN fallback or
visual position. Existing published Worker-direct views remain until the tested
replacement paths land. Durable Task/Callback identity, chief-executor journal,
recruitment snapshots and fenced temporary-supervisor recovery follow the spec's
persistence checkpoint. Chief-only augment implementation remains separately
scoped. Collaboration/YogiBox, remaining packs/review and final full Linux
shell/package parity are still later work. This runtime checkpoint does not close
the larger workspace/participant/conversation migration.


### Saved-source native fixture follow-up

Code checkpoint `4796c2d41da464a11dc89cbeffe3432bc4dd93e2` is published.
[CI 37214203913](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37214203913)
built Windows and passed Android, but its new Windows fixture failed when looking
up internal `CodexConnectionResult.Connected` with public-only reflection. The
fixture now explicitly uses non-public instance binding; no runtime behavior or
assertion is weakened. Startup/cold-layout and main-workspace GUI checks had
already passed before that fixture error. Local authoring verification also passes
all 39 checks. Check the corrected commit's CI before claiming native completion.

The corrected fixture in `c0cb91a3d5baea5b5d1fadc04d510c3a7e965291`
([CI 37214473997](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37214473997))
then passed initial adoption and exposed Windows thread affinity on delayed
cancellation: a caller without a WPF synchronization context resumed native cleanup
off the dispatcher. The adapter now explicitly dispatches the entire native
connection orchestration; the fixture waits for dispatched entry before cancelling
and retains late-candidate/retained-incumbent/reentry assertions. This is a runtime
adapter correction, distinct from the earlier reflection fixture correction.


### Verified saved-source handoff

`74f213610e1acc508269a11d885a2411e76c250e` matches published `main` and
passes all five jobs in [CI 37214689562](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37214689562):
portable, Windows, Android, Linux native and engine compatibility. Windows reports
**38 actual native workspace checks**, including initial saved connection, delayed
cancellation preserving the incumbent, reentry and the deliberately delayed startup
layout. Android compilation/package verification passes; physical Android remains
unavailable, not failed. Linux runs the actual SDL window, including the installed
saved-source action and injected late-provider cancellation/reentry.

Local campaign: **1,265 editor-pack checks**, **225 Studio checks**, **39 authoring
checks**, independent engine isolation and the full explicitly selected consumer
campaign pass. Native Windows source compiles with zero warnings/errors; actual
GUI evidence is the CI above. Provider/credential tests use injected fixtures;
real API billing, external login/authentication, production credential writes and
live Codex account/history restoration were not exercised. Images, runtime output
and logs remain outside Git. The superseded promotion stash remains intact.

No SDK, authorization or current CI blocker remains. Remaining implementation is
explicit Helper supervision assignment/migration and the common Helper request/
conversation UI, then durable Tasks/Callbacks, chief journal, recruitment snapshots
and fenced temporary-supervisor recovery according to the confirmed contract.
Existing Worker-direct UI/promotion routes are still present pending those tested
replacements; preserve old records while removing their send/promotion entry points.
Linux's saved connection boundary is verified but is not yet wired to that future
Helper request UI. Collaboration/YogiBox, remaining packs/review and final full
Linux shell/package remain subsequent stages. Chief-only augments are separately
scoped; do not expand into instant-table/render-authoring features.

### Explicit supervision assignment and recoverable identity checkpoint

The continuation after verified `d93cdd6387c1bd3d24d001865b9b1f95153c6cde`
([all-five-job CI 37215008985](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37215008985))
implements checkpoint 3 of `HELPER_SUPERVISION.md`:

- Public participant identity now distinguishes `AiRole`,
  `SupervisorParticipantId` and conditional `SupervisorRevision`; none grants
  chief-executor, augment or private-memory authority.
- Collaboration saves use version 3 and still read versions 1/2. Older readers
  reject new documents rather than dropping supervision fields on later saves.
- Installed `CoreTools/StudioSupervision.cs` owns cancellable, repeatable migration
  of owned active/archived records, exact owner-driven assignment, stale-revision
  rejection, and pending-work safeguards. Missing supervisors stay recoverable;
  there is no MAIN/name/position/provider fallback or automatic respawn.
- Helper disconnect archives the project identity; explicit rejoin restores the
  same ID and revalidates saved permissions against current owner grants. Foreign
  archives stay untouched. Histories, origin, global memory, Worker identity and
  work/request IDs remain intact. Persist/observer failure compensates both lists.
- Assigned Workers prevent accidental supervisor removal. Native initialization
  invokes common migration; malformed records produce the pack's recovery notice
  without preventing unrelated project editing or granting execution authority.

Local verification: **1,433 editor-pack checks**, independent engine isolation,
**225 Studio checks**, **39 authoring checks**, full explicit consumer campaign,
and actual SDL role/assignment/removal/reentry interaction pass. Latest Windows
net48 native source/test build has zero warnings/errors. Remote exact-head CI is
checked after publication. Logs/screenshots remain outside Git under
`/workspace/scratch/supervision-*`; verification uses injected AI/credential
services, with no real authentication, paid inference or credential writes.

Next is checkpoint 4: installed Helper-facing conversation/character actions and
real internal-Worker request integration across Windows, Android and Linux.
Replace Worker-direct send/promotion entry points only with those tested routes,
keep owned legacy history recovery, and add workload dots/counts and semantic
object Helper indicators. Durable chief directives/journal, recruitment snapshots
and command-fenced temporary-supervisor recovery remain the following persistence
checkpoint. Assignment revisions in this checkpoint are not distributed command
fencing. Keep Main Helper's ordinary supervisor abilities plus chief authority;
augments remain chief-only and separately scoped. Preserve the superseded promotion
stash; do not apply it. No executor recovery/reset was required after the transient
disconnection notices.

### Internal Helper request routing foundation

Assignment/migration commit `c8a40c727528089cd25c937064ae3fe76f8f7e63`
passed all five jobs in [CI 37217424958](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37217424958).
The next small checkpoint adds installed `StudioHelperRequests`, exposed only
through the trusted `IEditorStudioActions.HelperRequests` factory. Construction
is inert. Explicit Begin reuses a suitable idle assigned Worker or recruits an
internal Worker under that exact Helper/source. It does not adopt unassigned
legacy Workers, execute a provider or read/rewrite histories. Recruitment saves
before returning and compensates persist/observer failure. New permission grants
are bounded by both Helper and current owner grants.

Request leases capture exact source/identity/assignment revision and revalidate
current access, enabled profiles and Work permissions. Cancellation keeps the
Worker reserved until asynchronous cleanup; two routers for the same project hub
share reservations within the installed generation. The owning project runtime
must retain its router across view reentry and supply the living-Worker predicate
across pack reloads. This is not distributed command fencing or an edit lock.
Project disposal cancels every request even if a cancellation observer fails.
Private context uses only the explicitly supervising Helper's global and current
project memories. Read-only workload remains available with AI execution disabled.

Structural boundary: this is the routing foundation, **not** the shared
conversation implementation. No platform conversation route is switched yet.
The next implementation must own provider orchestration, timeline/history,
review/cancel/incident integration and Helper message identity inside CoreTools,
then mount identical common views on Windows/Android/Linux. Preserve the existing
native review/Yogi/incident subsystems as service boundaries until their own
migration. Do not implement a common send button that simply invokes native
`RunWorker`/`RunMobileWorker`, and do not remove old send/promotion routes before
the tested replacements exist. Source selection must go through installed
SavedAgent; plain text must not capture global hover/selection; actual Linux
execution must use the same request path. Durable Task/Callback/chief journal and
fenced supervisor recovery still belong to checkpoint 5.

Verification for this foundation: **1,535 editor-pack checks**, independent engine
isolation and the full explicit consumer campaign pass. Actual Linux SDL regression
passes; the new router is exercised through the installed factory on all three
portable platform variants, not yet through a new native conversation UI. Native
Windows source/reference build and exact-head remote CI are checked at publication.
Logs remain outside Git at `/workspace/scratch/helper-routing-*`. No provider was
connected and no paid inference, authentication or credential saving was performed
by the routing tests. Existing saved-Agent native fixtures remain injected.

Routing foundation `907cef00d60fd79c39eb0fcce79f7f9bf620a23b` matches published
main and passes all five jobs in [CI 37218947706](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37218947706).
Local Windows net48 native source/reference build also passes with zero warnings
or errors. Physical Android remains unavailable; remote Android build/package
passes. No new native Helper conversation route was claimed by this checkpoint.

### Hands-on shell regression checkpoint

The user reported an absent arbitrary-project open route, Codex consent/failure
followed by provider switching leaving controls locked, an indistinct required
project-name field, and clipped sidebar circles. Prioritize these reports without
changing the intended rail dimensions, two-column composition, section grouping,
or overflow policy. No max-count/+N design was approved.

The shared Helper execution WIP is preserved in stash
`7f620199f7187beac6300ff997fc6b87e9dfa465` (1,607 portable checks and isolation
passed; native conversation integration is not yet installed). Restore that
approved work after this regression checkpoint, preserving its spec and tests.
Do not apply the older superseded promotion stash
`3408b1e2c61752975aca66810dbc9d468d44aee4`.

Screenshot references `libfile_6fe4846ad5188191957196098c945c39` (form) and
`libfile_3bdd826592f48191a96ff237620a7ede` (sidebar) resolved in Library. The
required transfer helper on this executor was blocked by the network proxy with
HTTP 403, including an approved unsandboxed retry. This was a network download
failure, not an approval-review rejection. The screenshot pixels were unavailable;
do not claim to have inspected them. Extracted text alone was insufficient for
visual conclusions. Local/native reproduction and source measurements are separate
evidence.

Confirmed source findings and corrections:

- Shared home offered only new/recent project cards. Installed CoreTools now owns
  an explicit existing-project path form, accepting a manifest or an explicitly
  chosen folder containing exactly one top-level manifest. Missing/ambiguous
  paths report an error before native activation. No bundled project inference
  or data migration occurs. Linux's duplicate host-owned path controls are
  removed in favor of this same action. Native Windows and SDL checks enter the
  common controls to open an existing project.
- The name field had no placeholder and validation silently disabled Create.
  The common form now identifies the required name, displays a placeholder and
  nearby name-validation feedback, and clears it when the name is valid.
- The 96-pixel sidebar's two 48-pixel cells inherited additional grid/card/portrait
  margins; native Windows also added horizontal wrapper margins inside its fixed
  rail. Remove these duplicated margins, retaining the declared widths, circle
  size, two columns, section structure and vertical scroll policy. Native bounds
  coverage includes a visible vertical scrollbar.
- The reported consent lock has **not been reproduced** in the current installed
  action with injected delayed Codex preparation failure, Claude/Codex switching,
  fresh consent and late-provider cancellation. Portable recovery checks pass;
  an actual WPF control sequence and reconnect check are added. Do not label this
  as a fixed live Codex/authentication bug without reproducing it. No real auth,
  paid inference or production credential writes are allowed during verification.

Local checkpoint verification: **1,616 editor-pack checks**, independent engine
isolation, and actual Linux SDL smoke pass. SDL inputs open an existing project
outside recents, reject an empty path, clear the cancelled form on reentry and
preserve the manifest bytes. The SDL fixture initially split non-ASCII text by
UTF-16 character count; its input driver now chunks by SDL's 31-byte UTF-8 limit,
so the test exercises intact native text events. The generated Linux sidebar
screenshot was inspected: both columns' circle outlines fit the existing rail.
This is local fixture evidence, not inspection of the unavailable user images.

The full explicit consumer campaign also passes from a clean external copy.
Native Windows net48 source/reference compilation passes with zero warnings
and errors. Published `07843fae155df59a0b34104348cc1e006063bccc` passed all five
jobs in [CI 37221878091](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37221878091),
including **52 actual Windows workspace checks**. Native WPF recovery controls,
fresh-consent reconnect and two-column circle ink with a visible scrollbar pass.
Android build/package passes; physical Android interaction remains unavailable.
The reported live Codex failure remains unreproduced. Continue Helper integration.

Approved Helper execution work from stash `7f620199f7187beac6300ff997fc6b87e9dfa465`
is now restored after publishing the regression checkpoint. The stash is retained
as a recovery copy; new runtime implementation and native integration remain WIP.

### Shared Helper execution implementation

Installed CoreTools now implements the `HelperExecution` factory and owns saved
source connection, provider/tool lifetime, request preparation, per-tool authority
revalidation, cancellation and Helper-authored results. Hosts supply configured
provider/OS services, dispatch, explicit scope capture, and existing review/image/
pack/incident boundaries. The runtime does not call a native Worker-send handler.
The existing ChangeReview constructor remains compatible; the new overload lets
this path publish neutral Work activity without copying private prompts.

Plain text temporarily excludes global pointing and restores it after context
preparation. Frozen attachments, exact source/assignment, global and project-local
Helper memory, and recent bounded history enter only that request. Memory save
failure restores the previous private state. Candidate adoption rechecks both
lease authority and the captured history/access settings. Every provider tool
entry rechecks authority. Cancellation retains Worker reservation through cleanup;
urgent interruption retains a suspended checkpoint. Concurrent requests receive
distinct internal Workers and canonical request IDs. Failed review retains the
answer without applying the proposal. Results post as the supervising Helper.

This is an independently tested runtime foundation: **1,691 editor-pack checks**
pass with the shell regression checks restored, using injected providers and
credentials plus the real bridge/workspace/review objects on each platform variant.
Independent engine isolation and the full explicit external consumer campaign
also pass. Native conversation routes are not yet
switched; no paid inference, authentication or real credential writes occurred.
Windows net48 source/reference compilation also passes with zero warnings/errors.
Exact-head publication CI follows before completion claims.
The next change must add shared conversation/history views and native adapters,
retain readable legacy histories, preserve review/Yogi/incident behavior, and only
then remove direct Worker-send/promotion entry points. Durable Task/Callback/chief
journal and fenced recovery remain the subsequent persistence checkpoint.

Structural review added an optional authority guard to the shared review boundary.
Existing constructor overloads retain their ABI/behavior. The Helper execution
path supplies its captured lease/access predicate, checked immediately before
selected file application and before each subsequent command/retry. Revocation
while a human review is pending must fail before writing, not merely report a
failure after applying. An injected revocation test verifies that the previous
applied-count stays unchanged and the useful answer is retained.

Shared execution commit `5d44a90793687b2e52edab5a3d498ef38fe3df37` matches remote
main and passes all five jobs in [CI 37222489660](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37222489660).
The next local work adds installed `HelperTimelines`, with a private versioned
Helper/project/owner envelope and storage compare-before-write boundary. The
execution object and timeline controller belong to the project, while views will
subscribe/unsubscribe independently. Timeline tests are in progress; native
conversation adapters and common view definitions are still pending. Keep both
recovery stashes until this replacement is fully verified. No paid AI/auth or
production credential write has been performed.

Historical steering before the finalized specification below: the consent/provider-switch lock remains
unreproduced, even though injected native recovery checks pass. Other-Chat layout
sketches are review-only. A possible removal of the standalone stored YogiBox
vault/menu has **not** been authorized here. Reconcile that direction with the
parent before migrating the storage/menu UI; preserve historical boxes/messages
and temporary context transport. Continue independent conversation adapters.
The later finalized specification explicitly authorizes the layout and temporary
YogiBox changes; this earlier restriction no longer controls remaining work.

Local timeline/controller checks passed **1,751**, then **1,763** with the first
common conversation view mounted through all three portable backend variants.
Current edits preserve the existing question/answer navigation composition in a
shared definition. An editor-only bubble radius is interpreted by the three
native backends; existing readonly widgets keep their old defaults. SDL horizontal
stacks now honor explicit child widths instead of dividing fixed-width bubble
and arrow elements into equal cells. These latest layout adapter edits are being
reverified. They are not adoption of the external layout sketches.

The timeline owns drafts, current exchange, request routing/cancellation, receipt
selection, private-history policy/identity/serialization and recovery. Views can
be disposed and remounted without cancelling execution. Unknown versions stay
untouched; failed writes retain new in-memory results; compare-before-write rejects
unseen storage replacement. Foreign ownership immediately removes private drafts,
questions and image lookup. Old native histories remain untouched; their readonly
recovery UI still needs integration. The view includes temporary attachment
inspection/removal only, without changing any stored YogiBox service/menu.

Remaining before this checkpoint can replace native conversations: connect project
lifetime execution/history services and common character views on Windows/Android/
Linux; preserve native review/incident/Yogi queues and legacy-history recovery;
update internal Worker visibility/workload and semantic Helper indicators; test
actual native input, cancellation/reentry/failure and review paths. The current
native conversation routes remain active while this replacement is incomplete.

Actual SDL integration fixture now mounts the common view against the installed
Helper execution runtime, real bridge/workspace and an injected provider/storage
boundary. Native send reaches Helper-authored delivery; close/reentry keeps the
same pending request; explicit cancel disposes a late provider without globally
blocking editor input. The fresh-state run passed in 86 seconds, including all
prior shell/semantic/DLL checks. Generated screenshot was inspected: rounded
question/answer bubbles and arrow widths are correct. It exposed a too-small text
box hiding workload dots and left-aligned history dots; current edits give the
dots sufficient text bounds and center the navigation group. Native bounds checks
now cover those details. This is a fixture mount, not production adapter completion.

Earlier runs hit the old 90-second timeout while still progressing through prior
profile/workspace checks. The reused local verification state had accumulated 56
project entries, including abandoned timeout fixtures. The smoke tool now assigns
an independent temporary XDG state/cache per invocation, retains stdout/stderr on
timeout, and allows 180 seconds for the expanded full suite (fresh run measured
86 seconds). No assertion was removed and no user state is deleted. A WPF fixture
also now mounts this same common view/runtime and checks readonly bubble scrolling/
copy, send, close/reentry and late-provider cancellation; compilation/CI pending.

### Finalized UI/context direction accepted during the timeline checkpoint

The full user source is preserved in `FINAL_UI_CONTEXT.md` (all 798 source lines,
Library `libfile_7eaa25f2febc8191bcc0bc2aa46a14df`). The user finalized the document
and resolved Esc as whole-draft discard, identical to overall ×. These instructions
supersede earlier unapproved-sketch and unresolved-YogiBox notes; retain those
notes only as checkpoint history.

Finish verification/publication of the current project execution/timeline/view
foundation safely. It is **not** the complete final Helper lifecycle: the new
project-scoped runtime is for internal Worker execution. Next, separate global
Helper chat from optional captured project context. No synthetic/hidden project,
automatic open, provider request on mount, or fallback to an unrelated project is
acceptable. A Helper can chat with global memory with zero open projects; Workers
remain explicitly bound to owning Helper, Project and Task. Context captured at
request time must not drift to another project after exit/switch. Project Chat
belongs to the active project: Main Helper and humans continuously receive it;
other Helpers receive it when tagged. Preserve semantic locality and credentials.

Sequence remaining accepted deltas without redoing verified fixes:

1. Global/project-optional Helper lifecycle, global private history/context and
   native adapters; retain owned legacy-history recovery and existing review/
   incident execution. Remove Worker-direct/promotion routes with those tested
   replacements, add Helper workload dots and semantic ownership indicators.
2. Shared layout/actions: first project-add slot contains new/open side by side;
   retain verified existing-open validation. Add explicit connection status/retry,
   creation description visibility/focus and missing-name focus to the existing
   required-name feedback. Wider collapsible Agent/Helper/Player rail with roughly
   two rows and count overflow is now authorized. Keep hover/pressed text legible.
   Project hamburger/exit, lower-right rounded internal grid navigation/mailbox/
   Run, submenu edge handling, unresolved-count/highest-severity badge, and bottom
   Project Chat/Logs tabs plus latest meaningful log follow the same pack ownership.
3. Temporary Yogi compose/seal/drag lifecycle: source previews/navigation, per-item
   × versus whole-draft ×/Esc, double-click unseal, new-item automatic unseal, empty/
   invalid-send rejection, and draft clear on project exit. Remove the standalone
   vault/menu route without purging historical boxes or delivered attachments.
4. Actual launch → explicitly selected Golemancer open → enter → Run survival
   coverage, then final native/platform review. No bundled-project inference is
   allowed in runtime code; the test supplies the consumer path explicitly.

The fresh native diagnostic exposed .NET's fallback to the working directory when
an XDG directory does not exist. The verification tool now creates both temporary
XDG directories before launching. The accidentally generated smoke-only `Confectory/`
state was moved intact outside Git to `/workspace/scratch/helper-smoke-fallback-state-20261004`;
no user data or historical records were deleted. Python bytecode from syntax
validation was removed from the repository; no .gitignore changes were made.

Current foundation verification: **1,769 editor-pack checks** pass. The new
identity-substitution regression pins an open timeline to its original Helper and
owner, preventing a rebound participant from transferring private questions or
saving them under another Helper. History-consent changes preserve unsent local
input while disabling stored-history read/write/context forwarding. Independent
engine isolation and the full explicitly selected external Golemancer consumer
campaign pass. Actual SDL verification passes all existing flows plus shared
conversation send, close/reentry, pending-source cancellation and late-provider
disposal. Screenshot inspection also caught and corrected clipped history-dot
glyphs; a second SDL run and inspected screenshot confirm centered visible dots.
Screenshots and logs remain outside Git under `/workspace/scratch/`. Windows
net48 source/reference and native-fixture compilation pass with zero warnings or
errors. Actual WPF execution and Android compilation remain exact-head CI checks;
no physical Android device, paid model, external authentication or production
credential storage was exercised. Production conversation adoption and the global
Helper lifecycle remain the next implementation stage, not completed parity.

Published foundation `5cba2dfb48cf163aa9be776fbb794b125fdfcf9c` matches remote main.
All five jobs pass in [CI 37226496334](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37226496334).
Windows actually ran **58 native workspace checks**, including the shared Helper
bridge, readonly bubble/copy, view close/reentry and late-provider cancellation.
The next contract is `GLOBAL_HELPER_CHAT.md`; current local implementation work
adds genuine project-free execution and scoped global timelines. Native production
entry points have not yet switched to that controller.

Current project-optional implementation passes **1,865 portable editor-pack checks**.
`GlobalHelperExecution` constructs neither a project nor an EditorSession for
global chat. It resolves the exact global Helper/source and exposes only its
global-memory tool. Explicit Yogi snapshots remain usable; unresolved EY references
are reported without opening their projects. One global provider conversation per
Helper stays reserved through cleanup, while another Helper remains usable.

Explicit project bindings must contain the installed workspace and execution
actions for the same session, private directory and generation. Send captures that
binding and delegates to the existing internal Worker runtime. Global timelines
store each turn's project identity, filter model history to global/current-project
turns, persist private read receipts, and acknowledge only selected deliveries in
the active matching project. Exit/switch cancels captured project work without
opening another project. Urgent cancellation preserves the existing suspended
Worker/checkpoint state rather than relabeling it as an ordinary cancellation.

Portable checks cover no-project mount/send, invalid input, source failure/mutation,
access revocation, memory-save compensation, retained capabilities after completion,
close/reentry/late-provider disposal, independent Helpers, cross-project locality,
binding mismatch rejection, read receipts and urgent suspension. Engine isolation
and the explicit external Golemancer campaign pass. The actual SDL global fixture
also passes and its screenshot was inspected; the final-source SDL recheck passes.
Windows net48 source/reference and native-fixture compilation pass with zero
warnings/errors. Its native fixture now includes the same global mount/send;
actual WPF execution and Android compilation will be checked in exact-head CI.
These are fixture mounts. Production Windows/Android/Linux entry points, private
storage/consent services, review/incident adapters and legacy-history recovery are
still the next stage; current native conversation routes remain active.

Published project-optional runtime `196f675c02b09c921cb728942b51c6a15cb2fdfa`
matches the verified checkpoint. All five jobs passed in
[CI 37228676034](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37228676034),
including **60 actual Windows native checks**, portable, Linux, Android APK and
engine compatibility verification.

The next host-services increment passes **1,904 portable editor-pack checks**.
The installed directory owns explicit global history consent and save rollback.
Per-request consent prevents both context forwarding and later archival of a
history-disabled request, including project requests displayed in a global view.
Private file storage separates Helper/global/project identities and rejects stale
replacement; placement is pack-owned and persists only under history consent.
Native scope capture runs with ambient pointing cleared; the pack restores the
original pointing afterward. Provider transport lives outside profile experience
folders, so readonly experience browsing does not enumerate transport state.

Windows now mounts the common floating Helper view through existing Helper answer
and selection entry points. The native adapter supplies input, storage, transport,
review and existing incident cancellation boundaries. Reviews register before
tools run, resolution links update the shared exchange, and completion/unbinding
cleans registrations and cancellation proxies. New native verification exercises
the production adapter with injected services: null-session mount/send, close and
reentry, late connection cancellation, explicit project binding and clash linkage,
explicit rejoin after completed Workers are unassigned and the Helper removed,
and exclusion of private Helper overlays from automatic project Yogi captures.
Compilation is separate from actual Windows execution; exact-head CI must verify
the latter. The home sidebar still needs the shared project-optional entry update.

Local final-source checks: engine isolation passes; Windows net48 source/reference
and native verification compile with zero warnings/errors; actual SDL smoke passes
including project and global Helper requests, close/reentry and cancellation. Its
global conversation screenshot was inspected. This is still a Linux fixture mount,
not Linux production adoption. The full explicitly selected external Golemancer
campaign passes. Actual Windows execution and Android APK
verification remain exact-head CI checks; no physical Android device was tested.

Remaining adoption: Android document-only execution/review boundaries; Linux
production overlay/input/review services; common sidebar/global entry actions;
readonly legacy history before Worker-direct/promotion removal; Project Chat and
finalized layout/navigation, temporary Yogi lifecycle and explicit selected-project
Run survival. The accepted FINAL_UI_CONTEXT scope remains active. No paid inference,
external authentication or real credential writes are authorized for verification.

Windows host-services checkpoint `6cab6b98b4fc0f63e6e5bf0420c95a7d9a98e3d3`
was published by ordinary push with remote main verified and a clean tree.
All five jobs passed in
[CI 37231186874](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37231186874).
Windows executed **70 native workspace checks**, including the production Helper
adapter, clash linkage, safe rejoin and automatic Yogi private-overlay exclusion.

The next Android increment mounts installed Helper conversations through existing
Helper answer/selection paths. A separate native layer survives explicit project
changes and is excluded from automatic screenshots and semantic element picking.
Host services provide captured consent/options, private file storage, native input,
existing mobile review coordination, peer handoff and urgent interruption proxies.
The installed execution pack captures platform command availability, omits build/
project tools when unavailable, and rejects direct build/project/editor-build calls
before staging. Document editing and review remain available; no desktop command
capability is silently added to Android.

The official Microsoft Android workload installed successfully in this workspace.
Repository-supported Android dependency setup then failed manifest TLS certificate
validation; the installed Java runtime also lacks JDK tools such as `jar`. Full
local APK execution is therefore unavailable. Source compilation against the
official workload's Android reference assemblies passes with zero warnings/errors
(platform/deprecation analyzers are excluded from that reference-only harness).
Exact-head CI must still build and validate the real Android APK. No physical
Android execution, paid inference or external authentication has been performed.
Shared portable verification passes **1,919 checks**, including omission and direct
rejection of unavailable platform commands without queued work. Engine isolation,
Android source/reference and Windows net48 source/native-fixture compilation pass.
Android validates the next project/session and roles before detaching the current
conversation binding. The full explicitly selected external Golemancer campaign
and actual Linux UI smoke pass for this increment. Android floating-view sizing
uses the pack renderer's declared dimensions rather than a separate native width.
Linux production integration and the accepted remaining shell/
Yogi scope continue next; neither checkpoint establishes complete shell parity.

Android production Helper checkpoint `323ac6c39e905790a62fe01369933c01c8ddbfa5`
was published with remote main verified and a clean tree. All five jobs passed in
[CI 37232438441](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37232438441),
including the real Android APK build and 70 actual Windows native checks.

### Shared selective review prerequisite

The next Linux conversation integration requires a complete selective review,
beyond its prior apply-all/reject-all page. [SHARED_REVIEW_CHOICE.md](SHARED_REVIEW_CHOICE.md)
records the contract. The installed CoreTools factory now owns the common review
view, grouped file/hunk selection, compound items, excerpts/context/full text,
image previews, counts, empty finish, comparison and cancellation actions.
Windows/Android Helper review mounts retain their existing conflict callbacks,
peer handoff, scoped review and application/build-retry boundaries. Initial
comparison finishes before those OS dialogs open, so an active conflict dialog
remains accessible. Existing legacy review entry points remain intact.

Linux mounts the same choice as a modal input/render layer and uses it for existing
pack-command changes. Its current disk comparison can rebase disjoint edits;
unresolved overlapping edits remain an explicit cancellation/retry path. Linux
production Helper execution still needs the collaboration conflict callbacks and
persistent conversation overlays; the review increment alone does not establish
that adoption or Linux shell parity. No edit lock or new Supervisor system is
introduced. Unconfirmed selections do not apply documents.

Verification for this review increment: **1,976 portable pack checks**, including
hunk-validation rollback, failed/retried comparison, stale proposals, permission
loss, empty finish, cancellation, late comparison and reentry. The actual SDL
production review mount passes native pointer input while the surrounding command
is busy, failed validation without writes, empty finish, cancel/reentry and exact
reviewed application. The rendered review screenshot was inspected and remains
outside Git. Engine isolation and the full explicitly selected external Golemancer
campaign pass. Android reference-source compilation and Windows native-fixture
compilation pass with zero warnings/errors; actual Windows execution and the real
Android APK remain exact-commit CI verification, not local device claims.

Review checkpoint `b103c20a50a672c66e7848f530bf09b1e266bd26` was published
by ordinary push with exact remote main and a clean tree. CI 37234082534 passed
Android APK, portable and Linux-native jobs. Windows passed its existing 70 native
checks, then the new review fixture failed before inspecting the dialog: the
standalone native harness has no WPF `Application.Current`. The fixture now finds
the production dialog through its owner's `OwnedWindows` collection. No review
behavior or test assertion was removed; exact-commit Windows rerun is required.
The next global-sidebar entry work is preserved in a named recovery stash while
this correction is verified and published.

The b56b4e6 Windows rerun remained inside native interaction beyond its normal
duration; active-job logs were unavailable from GitHub. Source review identified
another harness mismatch: it called the asynchronous native dialog directly from
Main, outside a dispatcher frame. The fixture now enters through the same
`Dispatcher.InvokeAsync` boundary as the production Helper host and pumps until
the owned dialog mounts. Reviewed application also keeps the dispatcher pumping.
All original selection, cancellation, reentry and no-premature-write assertions
remain. Compilation passes; the exact-commit native rerun must confirm the result.
Engine source is unchanged from the passing isolation and full external consumer
campaign recorded above. Global-sidebar WIP remains preserved separately.

Windows review verification now passes **76 actual native checks** on
`6da2422821f3e3a06a1af20786ff51258d3d3d38`, including the production common review
mount, complete before/after text, empty finish, cancel/reentry and reviewed
application. [CI 37235465034](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37235465034)
passed all five jobs. The dispatcher fixture correction is confirmed;
the earlier b56b4e6 run's native interaction had stalled rather than completed.

### Project-optional sidebar entry

The installed sidebar now resolves local Helper cards and owned joined-Helper
portraits to the application-owned conversation, including a genuine sessionless
home. Opening and Yogi drops do not join a project, create a Worker or call a model;
Join remains part of an explicit project-bound send or explicit workspace Join.
Sidebar closure hides the same private view without cancelling its active request.
Foreign Helper identities cannot resolve a matching local private profile and keep
the existing project-public route. Attachments are validated and copied into the
local composer. Windows and Android hosts only mount/hide the installed views.
Linux's native conversation capability remains explicitly unavailable pending its
persistent-overlay/execution adapter; the common policy is tested for Linux too.

The entry increment passes **1,994 portable checks**, Windows native-fixture and
Android source/reference compilation, engine isolation, the full explicitly
selected external Golemancer campaign and the actual Linux UI smoke. A Windows
production fixture now opens/closes the Helper through the installed sidebar with
no session and checks preserved active work/draft. Its execution remains an
exact-commit CI check. Legacy Worker-direct/promotion routes remain available until
readable legacy history and replacement interactions are tested. Linux production
Helper integration, Project Chat, finalized shell layout/navigation, temporary
Yogi lifecycle and explicit project-entry/Run survival remain active next work.

### Temporary Yogi recovery and adoption (2026-10-05)

Usage-limit recovery confirmed local and remote main at `160f53b`, with no later
local commits. Four named recovery stashes and all unpublished Yogi files were
preserved. The unpublished draft/view increment completed **2,066 portable pack
checks** before interruption; that count is distinct from the published Windows
native coverage. A recovery archive was retained outside Git before continuation.

[TEMPORARY_YOGI.md](TEMPORARY_YOGI.md) specifies the temporary application draft
and immutable attachment inspection. Installed CoreTools owns its state, validation,
composition/inspection definitions, item removal, seal/unseal, edit-copy and clear
commands. There is no persistence/project capability in the draft. Candidate
captures are validated atomically, new items unseal the existing identity, and
stable row identities prevent a stale item action removing another row. DTO copying
clones mutable metadata/containers while retaining immutable image strings, avoiding
capture serialization on each composition keystroke. Inspector host boundaries
receive copies and cannot mutate historical attachments.

Windows and Android adoption now mounts those shared views. Capture/navigation,
native image decoding/display and OS drag input remain platform boundaries. New
drafts no longer save collaboration vault records. Global attachment editing works
without a project. Whole-box ×/Esc and project cleanup clear only the temporary
draft. Legacy vault bytes and delivered attachments remain readable. Native
verification and remaining Linux adoption are pending for this local increment;
do not infer all-platform completion from portable or reference compilation.

Next: verify/publish this coherent Yogi increment, add Linux production persistent
Helper and Yogi overlays with actual SDL input, preserve existing conflict callbacks,
then readable legacy recovery and Worker-route retirement, common Project Chat and
finalized navigation/layout plus explicit selected-project entry/Run survival.
Subsequent durable supervisor recovery, instant table, augment and render authoring
remain future authorized checkpoints; none is silently removed from the sequence.

Current temporary-Yogi source verification passes **2,075 portable pack checks**,
engine isolation, and the full explicitly selected external Golemancer consumer
campaign. Android source/reference compilation passes with zero warnings/errors;
Windows native-fixture compilation passes, while its added production Yogi input
assertions require exact-commit Windows execution. Actual Linux regression UI verification passes all existing SDL input/review/Helper
fixture flows; production Linux Yogi/Helper mounting is a subsequent increment.
The Windows capture adapter rejects a delayed render-frame capture if Esc or
project switching invalidated its gesture, preventing cleared drafts from being
repopulated by a late screenshot. No paid/provider-auth/credential writes occurred.


### Linux production continuation — local verification in progress

Shared conflict-choice specification and installed CoreTools comparison/actions are
implemented with separate native Linux overlay/input/dispatch adapters. Helper
conversation capability is separate from legacy Worker conversation capability.
Linux production global/project Helper services now use the installed execution,
scoped pack tools, existing collaboration review coordinator and selective review.
Temporary Yogi composer/inspector, native image viewing and frozen drag delivery
use the installed definitions; global views remain project-free.

The expanded portable suite passes **2,117 checks**. The first actual SDL run
failed an obsolete assertion that all Linux conversations were unavailable; its
replacement asserts the installed Helper capability. Production SDL close/reentry,
delayed-provider cancellation and conflict-selection tests are being run next.
These Linux changes are **unpublished** and require native verification, isolation,
explicit consumer verification and cross-platform source checks before a commit.
Remaining current-stage work: native project Helper review/incoming-edit callbacks,
legacy readable recovery and Worker-direct route retirement, shared project chat
with Main Helper routing, final common navigation/layout and selected entry→Run.
No paid provider/auth/credential verification has occurred.

The first expanded actual SDL production run now passes global entry/send, pending
close/reentry/cancel and late-provider disposal, sessionless temporary close and
explicit conflict candidate selection. Windows fixture source and Android official
reference-source builds pass with zero warnings/errors; independent engine
isolation passes. Selected-project testing exposed a Linux adapter ID-validation
mismatch: installed Worker IDs use `worker-…`, while Agent/Helper profile IDs are
GUIDs. The private transport adapter now validates the selected participant and
hashes its ID into a safe private directory segment. The corrected project send
and review registration pass. Active-draft tool handoff and native parcel lifecycle
checks are still being completed; do not claim their acceptance yet.

The parent's newly authorized subsequent function-contract/algorithm-tree and
schema-linked XML-help work is recorded in [AUTHORIZED_FUTURE_STAGES.md](AUTHORIZED_FUTURE_STAGES.md).
It is queued after existing stages and does not expand this increment.


### Linux production increment ready for publication

The installed pack owns conflict view/actions, Helper execution/timelines and
temporary Yogi composition/inspection. Linux supplies separate native renderers,
coordinate transforms, clipboard/image/capture/input adapters, captured provider
services and existing collaboration review coordination. SDL click-count metadata
now drives reliable parcel/sidebar double-clicks; the original four-argument
PushPointer API remains available. Typed raw document changes use existing human
working-copy/Room checkpoints, never file application before explicit review.

Completed local verification:
- **2,117 portable pack checks**, including all-platform installed conflict choice
  and separate Helper/legacy conversation capabilities.
- Full actual SDL regression pass, with **36 production Helper checks**: native
  home/sidebar double-click entry without a project, injected global send, delayed
  close/reentry/cancel and late provider disposal, visible failure and Ctrl+Enter
  retry, exact selected-project binding and review registration before tools,
  Helper-authored delivery, real scoped read/patch active-draft handoff, preserved
  human buffer/disk bytes, close-draft recovery, private capture exclusion, actual
  parcel drag/attachment/unseal and Esc retaining the attached copy. Installed
  conflict picker returns the exact explicit candidate without applying/resolving.
- Independent engine/button-pack isolation on net48/net10.0, timing/camera/external
  UI pack checks; full explicitly selected external Golemancer clean build and
  verification; Windows native-fixture source and Android official-reference
  source builds with zero warnings/errors.
- Evidence is outside Git under `/workspace/scratch/linux-production-helper-final*`,
  `linux-helper-packs.log`, and `linux-adoption-{isolation-final,consumer,windows-source,android-source}.log`.

No paid requests, external authentication or real credential saving were used.
Actual Windows execution/Android APK build for this increment await remote CI;
local Android APK prerequisites and physical-device verification remain unavailable.
The earlier f3 checkpoint's five CI jobs/85 native Windows checks remain confirmed.

Next: readable legacy recovery before retiring Worker-direct/promotion/creation
routes, common project chat and continuous Main Helper routing, final common
navigation/layout/mailbox/log panel and selected-project entry→Run. Broader shell
parity/package and the subsequent stages remain unfinished. This increment is a
checkpoint, not completion of the assigned common-shell stage.


Linux production checkpoint `cbed5eda4cf0fac1adda672ad67049aabdae3d21` is pushed,
exactly matches remote main and has all five successful CI jobs in run 37255043663.
The next contract is [LEGACY_CONVERSATION_RECOVERY.md](LEGACY_CONVERSATION_RECOVERY.md);
implement and verify recovery before retiring superseded direct Worker routes.
Shared public project/Room chat must preserve private-context exclusion while
adding Main Helper continuous receipt and explicit ordinary Helper mentions.

## Read-only legacy recovery checkpoint (2026-10-05)

Installed CoreTools now owns the common catalog and recovery view/actions on all
three hosts. Native adapters provide captured-project consent, read-only private
file IO and OS presentation boundaries. Active and archived owned identities are
readable even when their Agent source or Worker supervision is missing. No source,
assignment, provider conversation, receipts or tasks are reconstructed implicitly.
Files are never converted or written; malformed and oversized originals remain.

Validation: 2,189 portable checks; eight actual SDL recovery checks covering
archived history, pointer selection, private capture exclusion, consent revocation,
close/reentry and unchanged bytes/metadata; Windows native fixture and Android
official-reference source builds with zero warnings/errors. Windows execution
and actual Android APK build will be confirmed by exact-commit CI after publication.
Independent net48/net10 engine/button/timing/camera/external-UI isolation and the
full explicitly selected external consumer clean-build/verification campaign pass.
Local physical-device testing and actual local APK prerequisites remain unavailable.
No paid AI calls, real authentication or credential saving occurred.

Next work remains common project/Room chat, continuous Main Helper receipt and
explicit ordinary Helper tags; then verified retirement of superseded direct
Worker/promotion/creation routes and final common navigation/layout/mailbox/log
panel plus selected-project entry→Run. Internal Worker and conflict/handoff
services remain intact. This is a recovery checkpoint, not stage completion.
Evidence: `/workspace/scratch/legacy-history-{packs-publish,native,windows-final,android-final,isolation,consumer}.log`.

Recovery 5580f95 exactly matches remote main and all five CI jobs are successful.
The next contract is [SHARED_PUBLIC_CONVERSATIONS.md](SHARED_PUBLIC_CONVERSATIONS.md).
Its implementation remains local until meaningful controller/view/native checks
and the independent engine/selected-consumer campaign pass.

## Installed public conversation foundation (local verification, 2026-10-05)

CoreTools owns the selected-session public controller and chat/log view through
the installed factory. The private ABI supplies current consent, UI dispatch,
fresh provider service and explicit attachment capture boundaries. Main receives
new project public dialogue; ordinary Helpers receive explicit tags. Foreign
private sources and internal Workers are excluded. Historical messages never
replay on mount. Closing/reopening a panel preserves its local unsent composer
and pending requests. Room sends preserve same-Room human editing presence.

The live renderer matrix passes 2,354 checks, including queue ordering, explicit
foreign mentions, private-context/tool exclusions, exact displayed receipts,
immutable attachment/no-send, failure/retry, cancellation during connect/reply,
late-provider disposal, Main-role/source changes, Room routing and the existing
AI-depth boundary. Failed durable message writes roll back staged memory; a
post-persistence observer error exposes the accepted message identity so callers
cannot accidentally duplicate its human/AI post. Existing mutation observer-error
compensation checks continue to pass. No paid AI, actual auth or credential saving.

This is a factory/view foundation. Production native project/Room entry points
still need binding and removal of old native mention handlers, actual native
public-flow verification, the final wide lower panel/viewport, common navigation/
mailbox/Run and selected-project entry→Run. Direct Worker/promotion/creation UI
remains until its replacement is verified. The assigned stage is not complete.
Additional local checks pass: independent net48/net10 engine/button/timing/camera/
external-UI isolation; net48 CoreTools build; Windows native-fixture source and
Android official-reference source builds with zero warnings/errors; actual SDL
regression with 83 printed PASS checks. These native regressions preserve the
existing flows; new public entry points have not yet been bound or exercised.
The full explicitly selected external-consumer clean-build/verification campaign
also passes, including a copied project with no prior output.
Evidence: `/workspace/scratch/public-chat-foundation-{final,isolation,core48,windows,android,native,consumer}.log`.
Local actual APK prerequisites and physical-device execution remain unavailable;
remote actual Windows execution/Android APK build will be confirmed by exact CI.

## Native public conversation integration (local continuation, 2026-10-05)

Windows/Android project and Room entry points and Linux project chat now bind the
installed public controller/view. Native public provider/mention workflows are
replaced by controller observation. Controllers survive panel close/reentry and
cancel on captured-session disposal. Native hosts retain provider construction,
UI dispatch and explicit capture boundaries. Automated native verification requires
injected services before automatic public provider use; no paid AI/authentication
or credential saving is exercised.

Actual SDL production checks pass mount/no replay, physical send, Main routing,
ordinary explicit tags and continuous Main replies, close/reentry, failure/retry,
pending-connect persistence and cancellation with late-provider disposal. Temporary
Yogi/modal inputs retain precedence over public-panel Escape. Windows native
fixtures cover mount/send/reentry/connect-cancel with synthetic services; source
build and Android official-reference source build pass with zero warnings/errors.
Actual Windows execution and Android APK build require the next exact remote CI.
The Android device/runtime remains unavailable locally.

Current local evidence: `/workspace/scratch/public-chat-production-linux.log`,
`public-chat-windows-integration-source.log`, `public-chat-android-integration-source.log`.
Final repeated SDL/isolation/explicit-consumer checks use `public-native-*.log`.

The final wide lower panel/viewport, common navigation/mailbox/Run and selected
project entry→Run remain pending. Windows/Android embedded tiny project chat UI
still needs replacement. Retire superseded direct Worker/promotion/creation default
entry points only after the shared replacement is verified; preserve internal
Workers and legacy read-only recovery. This integration is a checkpoint, not
completion of the assigned stage. Do not repeat the published controller/view,
Helper/Yogi or recovery implementations.

Final local integration gates pass: actual SDL regression (96 printed PASS checks),
Windows native fixture source build (zero warnings/errors), independent net48/net10
engine/button/timing/camera/external UI isolation and the full explicitly selected
external-consumer clean-build/verification campaign. Agent management consults
active installed public operations instead of the retired native mention-token
set. Project detachment/whole-window close cancels those captured public operations.
Remote exact-head CI remains to be confirmed after ordinary push.

Native integration `8ad2fe54c2c4827d3d75caa9cb6b68e6c77c745f` exactly matches
remote main. [CI 37263928779](https://github.com/jorodane/The-Golemancer-s-Factory/actions/runs/37263928779)
passed portable, actual Linux and Android APK; the new Windows public-send fixture
timed out and engine compatibility was skipped. This is a real failed check,
not an unavailable platform. The follow-up makes fixture Main selection explicit
and checks installed composer/routing state before waiting, so a failure reports
its actual stage. The shared viewport continuation remains separate local work.

CI 37264402927 now isolates the Windows failure to public consent/lifetime gating:
the existing private-Helper fixture shuts down its own subsystem and sets its
private `helperClosing` flag. The public adapter had incorrectly reused that flag.
Public requests now use a separate actual-window closing flag; private Helper
shutdown does not disable the independent public controller. No consent check is
removed. Source compilation and the next exact CI verify this correction.

## Shared transcript viewport continuation (local, 2026-10-05)

CoreTools declares a fixed vertical transcript/operation viewport. Native Windows
ScrollViewer, Android ScrollView and Linux clipped layout/wheel/hit testing interpret
that same installed element. Composer, send, tabs and latest-log remain outside.
An installed earlier-history action exposes older public records without replaying
providers. Keyed view updates preserve native scroll. Windows receipts intersect
all clipping ancestors; Android observes dialog input after child consumption and
uses actual visible rectangles. Android log input supplies individual lines rather
than treating the full status transcript as a latest line.

Local portable matrix passes 2,360 checks. Actual SDL regression passes 99 printed
checks, including native wheel scrolling, clipping, composer visibility and the
existing public send/retry/cancel/reentry flows. Windows viewport fixture source
compiles with zero warnings/errors. Android official-reference compilation, full
independent-engine isolation and explicit external-consumer campaign are final
local gates. Actual Windows viewport runtime and Android APK will be checked by
exact-head CI after ordinary push. The final wide lower-panel placement and shared
navigation/mailbox/Run/selected-entry→Run are still pending, as are retirement of
superseded default Worker UI routes after replacement verification. No new supervisor,
instant-table/functions/augment/render-authoring workflow is included.

Final viewport local gates all pass: 2,360 installed portable checks; 99 actual
SDL regression checks; Windows native source and Android official-reference
source compilation with zero warnings/errors; isolated independent engine campaign;
full external-consumer clean-build/verification campaign with explicit selection.
Evidence: `/workspace/scratch/public-viewport-{portable,native,windows,android-touch,isolation,consumer}.log`.
No paid requests, actual authentication or real credential saving were exercised.
All five jobs for the preceding lifetime correction are now confirmed successful
in CI 37264710834, with 95 actual Windows native checks. The new viewport awaits
its own exact-head remote execution after ordinary push.

## Installed workspace navigation foundation (local continuation, 2026-10-05)

Contract: [SHARED_WORKSPACE_NAVIGATION.md](SHARED_WORKSPACE_NAVIGATION.md).
The installed factory now supplies common project header, mailbox/Run/internal-grid
bar and separate project/internal menus. CoreTools owns inert mount, menu actions,
current-session/busy guards, explicit one-shot project entry, Run/Stop selection,
readable execution failure/retry and stop-before-exit ordering. The ABI restricts
hosts to captured-project surface opening, folder IO and process/run boundaries.
All three renderer matrices pass 2,408 checks, including direct activation of
disabled controls, stale-session cancellation and no execution on render/remount.

Independent engine isolation and the full explicit external-consumer campaign pass.
Actual SDL regression passes after fixing startup reentry cleanup: a fresh startup
finishes an old home-animation clock before accepting input. An explicit assertion
forces this previous-transition case. Normal logo motion and saved-Agent skip
checks still pass. Windows/Android source and exact-head CI are remaining gates.
No paid requests, real auth or credential saving are used.

This is a definition/action foundation. Native header/toolbar/menu binding, wide
lower-panel placement, selected-project entry→Run and superseded default Worker UI
entrypoint retirement remain to be implemented/verified within the assigned stage.
Do not count this checkpoint as completion. Later supervisor/instant-table/functions,
augment and render-authoring features remain excluded.

Final foundation gates pass: 2,408 installed portable checks; actual SDL regression
with the forced previous-animation startup assertion and existing motion/skip,
private Helper/Yogi/recovery/public/viewport flows; Windows native fixture and
Android official-reference source builds with zero warnings/errors; independent
engine isolation; full explicit external-consumer clean-build/verification campaign.
Evidence: `/workspace/scratch/workspace-navigation-{portable,native,windows,android,isolation,consumer}.log`.
Viewport checkpoint 534e5fd is independently confirmed CI-complete in run
37265345014 (all five jobs, including 98 actual Windows native checks).

## Exact continuation and preserved native adoption draft

Implementation `cd0ee2ae1c5df59aba96fecbc085e9c42d1fcbe8` exactly matched remote
main and all five CI jobs succeeded in run 37267513899. This assigned stage is
still incomplete. Resume the native adoption; do not duplicate installed workspace/
participant/Agent/Helper/Yogi/recovery/public-controller/viewport/navigation work.

The unpublished 19-source-file draft is preserved in the named stash:
`WIP native shared navigation and lower-panel adoption after cd0ee2a`, exact stash
commit `a36c4121976412168747d8c5daa762ff2944feb4`. A second source-only recovery copy
is `/workspace/scratch/native-navigation-adoption-recovery-20261005.tar.gz`.
Older recovery stashes are retained. Apply the named stash once to continue; the
archive is an alternative recovery copy, not a second patch to apply afterward.

Draft contents: private `CloseMenu` ABI/action; CoreTools mailbox badge/severity
signals; native Windows/Android/Linux header/toolbar/menu adapters and explicit
selected-project entry wrappers; attempted lower public-panel placement; retirement
of Windows tiny sidebar chat and Android default sidebar-chat construction. Native
project execution remains behind captured-project host boundaries. The existing
Android launch/stop/activity lifecycle helpers were restored after a source-edit
boundary temporarily omitted them. Its latest official-reference build passes
with zero warnings/errors; these drafts have no actual native runtime verification.

Specific next checks/fixes before publishing that draft:

- Windows source build must be repeated after removing obsolete button fields and
  initializing the reflection-only executor override. The earlier draft build
  failed only those warnings-as-errors; it has not been represented as passing.
- Linux mailbox binding currently has an explicit temporary unsupported placeholder.
  Implement its actual installed shared UI/boundary; never ship that placeholder
  or substitute a generic fallback shell. Linux draft source/runtime is unverified.
- Complete actual body/lower-panel geometry and responsive clipping. Linux draft
  uses a provisional 520-pixel lower area and has not yet reserved body bounds.
- Add displayed-receipt, keyboard/IME, focus/suspend and Yogi drop handling to the
  embedded lower panels; preserve private overlays and temporary-draft Escape.
- Exercise production layouts and captured/injected Run/Stop/entry/error/exit
  boundaries on actual Windows and SDL, including busy and same-project reentry.
  Keep actual project process execution disabled during native verification.
- Retire superseded default Worker direct-chat/promotion/creation entrypoints only
  after replacement flows pass. Preserve internal Workers, stored participant
  records, original read-only recovery and existing clash/handoff callbacks.
- Repeat portable/isolation/full explicit external-consumer/source/native gates,
  publish small ordinary main commits and confirm exact-head CI before completion.

No paid AI, actual authentication or real credential saving was used. Android
physical-device execution remains unavailable; official-reference compilation and
successful actual APK CI are distinct evidence. Subsequent supervisor/instant-table/
functions/augment/render-authoring work remains outside this task.

## Urgent Windows participant lifetime correction (2026-10-05)

The reported existing/new-project crash is a native lifetime mismatch, not a
legacy-data-only issue. The old queued presence callback read the window's mutable
current session and then rendered Worker runtimes belonging to another session or
an already removed participant. Project opening replaced `session` before clearing
those runtimes; a nested WPF dispatcher pump during entry could reach `Unread` with
an ID absent from the new collaboration. Shared Join publishes complete Agent/
Helper identity before native attachment; retaining old native objects across that
publication is not a substitute for binding them to the selected collaboration.

The correction captures the originating session/hub and observation revision for
queued refreshes, aborts and revokes old observation before session replacement,
stops old pack work and clears native runtime objects before publishing the new
session. Public roles load before controls can trigger refresh. Worker runtimes
bind the exact session and Participant object, and refresh reconciles removed or
replaced identities before rendering/receipts. This disposes obsolete native
attachments; it does not remove collaboration records, reset permissions, create
missing participants, change `Require`, acknowledge hidden messages or rewrite
original histories.

The native regression creates a fresh project through the installed creation
view with a selected Helper, pumps an already queued old-project refresh inside
project-title change, opens an existing project with stored conversations, returns
to studio and reopens with late old-hub notifications, archives/restores a
participant during queued refresh and disconnects its source. It checks complete
fresh identity, stale runtime removal, intact original history bytes and continued
permission rejection for archived participants. Actual Windows execution is the
exact-head CI gate; local native fixture source compilation passed with zero
warnings/errors. Independent-engine and full explicit external-consumer checks
precede publication.

Common-shell adoption is preserved separately in stash
`ea8de665cb73c383293815e720e5fe621e3eb3cf`:
`WIP installed mailbox and native lower panel preserved before Windows participant crash fix`.
It includes the restored draft, installed mailbox UI/actions and preliminary
reserved lower-panel geometry/input changes. Those additions are not published or
fully verified. All prior stashes and the source recovery archive remain retained.
Resume that draft after this urgent correction's exact CI passes; apply only the
latest draft once, not every backup. Merge the new observation/runtime lifetime
fix into native adoption rather than overwriting it. Remaining mailbox coverage,
input/focus/Yogi/entry-to-Run checks and Worker entrypoint retirement still apply.

Exact fix commit `22088298c7117ccc2a4d91697d86e9fc179a56f3` passed all eleven
new lifetime/history checks on actual Windows in CI 37281628512. The overall
Windows job then failed because the older main fixture retained its original
`session` variable across this new test's actual reopen; attachment correctly
rejected that stale Participant object. The follow-up refreshes only that fixture
reference from the window's current session. Production ownership checks remain
unchanged. Android APK succeeded; other exact-head jobs are tracked separately.
