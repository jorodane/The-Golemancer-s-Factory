# Common editor shell migration

## Active task and resume instructions

The broader authorized goal is the complete common engine-pack UI migration.
This turn ends after the profile/global Helper checkpoint and its verified handoff;
workspace/conversation work must wait for the next worker. Repository synchronization
is an earlier completed request. On resuming after context
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
initial checkpoint. Linux Join remains disabled until its real worker/conversation
support exists. No fallback shell or deleted feature establishes parity. Resume
from this checkpoint without duplicating the preceding profile/global Helper work.

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
