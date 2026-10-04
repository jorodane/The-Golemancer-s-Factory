# Helper-facing supervision contract

Status: user-confirmed replacement direction, 2026-10-04. This supersedes the
Worker-direct-chat and Worker-to-Helper promotion direction in earlier shell
handoffs. It defines acceptance and sequencing; it does not claim that the
existing runtime already implements the replacement.

## Finalized lifecycle clarification

[FINAL_UI_CONTEXT.md](FINAL_UI_CONTEXT.md) is the latest accepted user specification.
A Helper is independent of project lifetime: it can chat using global memory with
no open project. Chat may capture the current project as optional context, but
opening a chat must never open or switch projects. The project-scoped execution
and timeline foundations below are internal Worker/project capabilities, not a
requirement that every Helper chat create a Worker or a project. Global chat must
use a genuinely project-free context/tool boundary; do not manufacture a hidden
project to satisfy an API. Preserve semantic locality when a project is present.

Project Chat remains active-project public communication. Main Helper and human
users continuously receive it; ordinary Helpers receive it when tagged. Worker
communication remains internal and explicitly Helper/Project/Task bound. Apply
remaining finalized layout/YogiBox deltas in the staged shell handoff; Esc and
whole-box × discard the current draft, preserving delivered attachments/history.

## Identities and authority

- An Agent is a device-owned provider connection. It is not a character, a team,
  a Task, or a grant of project authority.
- Every Helper is a supervisor of its own Workers and is the user-facing
  character/conversation identity. Its explicit global memories retain concise,
  significant context across projects. Request context stays semantically local;
  private histories and memory are not automatically published or copied.
- A Worker is an internal execution unit with its own running/cancellation/review
  state. Users request work through Helpers, not through Worker chat windows.
- Main Helper has ordinary Helper/supervisor capabilities **plus** chief-executor
  authority over other Helpers. It may recruit and assign Workers directly,
  including unassigned or cross-team work; it is not restricted to directing
  Helpers. Ordinary capabilities must not disappear when the chief role is set.
- Augments belong exclusively to chief-executor authority. Generic Work
  permission, a Helper role, Worker ownership, an icon, or visual placement cannot
  grant augment authority. The augment implementation is a later checkpoint.
- Team composition is dynamic. On a request, a chief executor can recruit a
  suitably specialized Helper when no available Helper fits the work. Do not
  introduce fixed team membership as the organizing rule.
- Role, assignment, ownership, permission and authority revision are explicit
  state validated by installed pack actions. Rendering and placement never confer
  control. Existing human/private-owner and project permission boundaries remain.

## Shared shell behavior

Identical installed engine-pack definitions/actions drive Windows, Android and
Linux. Hosts interpret native build/render/input/OS boundaries. Common C# outside
an installed pack does not satisfy ownership of shell actions.

The visible conversation and character surfaces center on Helpers. Remove user
entry points for direct Worker chat and Worker-to-Helper promotion as the new
routes are installed. Do not silently redirect a Worker conversation to an
unrelated Helper or create a Helper merely because an old chat is opened.

A Helper portrait/profile card shows its Worker count and compact state dots
below it. Hover/focus supplies brief activity such as working or editing an
object. These are workload indicators, not Worker chat launchers. Main Helper's
own Workers are represented the same way as another Helper's Workers.

Editable semantic objects show the owning Helper's icon when its Worker is
acting there. Resolve this through explicit assignment and scoped activity;
an icon is not proof of authority, a lock, or permission to overwrite. Foreign
private Helper assets must not be read by matching an ID; use an authorized
public projection or a neutral identity glyph.

Keep ordinary human/project communication and existing review/incident features.
Helper request routing must preserve real provider execution, cancellation,
review, per-request tool scope and incoming-change callbacks. Do not replace
missing Linux execution with fabricated answers or a simplified fallback shell.

## Durable work and chief journal

Task and Callback identities are independent of chat sessions. Changing a chat,
reconnecting a provider, recruiting/replacing a Helper or moving a character must
not silently create duplicate Tasks, lose callbacks, or reset active work.

When Main Helper issues overall directions under chief-executor authority, it
must record a chief-executor journal entry tied to the durable directive/Task
identity and relevant Helper/Worker assignments. The record must distinguish
ordinary Helper work from chief-executor directives and carry the authority
revision used. A failed required journal write must not dispatch an unrecorded
overall directive. Retries must not duplicate directives or journal effects.
Private credentials, raw private memory and unrelated conversation text are not
journal payloads.

Unconfirmed edits remain local. Incoming changes to an actively edited semantic
target use the existing clash/conflict callbacks. Do not add mutually blocking
object/file edit locks. Command authority fencing below is distinct from edit
locking and cannot suppress required conflict callbacks.

## Recruitment snapshots and supervisor replacement

On recruitment, persist the Helper's initial configuration: specialty,
personality/character traits and expression settings, intended role, relevant
initial execution settings and the applicable authority/scope limits. Give the
snapshot a stable identity/version tied to recruitment. Keep sensitive settings
in the appropriate private store; references to Agents do not copy credentials.
Do not use a mutable current chat transcript as the recruitment template.

If a supervisor disappears while the Main Helper/chief executor is available,
the chief executor can create a temporary supervisor with similar characteristics
from that saved configuration. This is explicit recovery, not a renderer fallback.

Before recovery, inspect authoritative assignment state, outstanding Tasks and
callbacks, active commands, and living Workers. Unreachable or timed-out is not
conclusive evidence of failure. Do not respawn living Workers, erase original
Helper state, discard pending work, or elect a replacement chief merely because
one supervisor cannot be reached.

Use an explicit, persisted authority handoff with a revision/epoch (or an
equivalent fencing mechanism). Assignments and commands must validate the current
authority revision; a returning original supervisor cannot resume duplicate
command authority. Reconcile its state and explicitly hand back or retain the
replacement. Preserve Task/Callback and living Worker identities throughout.
Authority handoff must be conditional on the previously observed assignment
revision and recorded with the chief's recovery decision. Failed handoff must not
leave two effective supervisors. Implement and test these guarantees at the
supervisor-persistence checkpoint, not inside native/sidebar adapters.

## Existing records and migration

No destructive record purge. Preserve Agent profiles and credentials, global
Helper identities/memories, existing promoted-Helper identities and their first
experience, Worker histories, pending edits, Tasks, incidents and review records.
Legacy origin/promotion metadata remains readable provenance, not an action to
promote again.

Use backward-compatible schema additions and explicit migration/recovery state.
A Helper identity must be distinguishable from an execution Worker, and a Worker's
supervisor assignment must be distinguishable from both its private owner and its
provider source. The implementation checkpoint must specify stable keys and
migration rules before writing records.

Unassigned legacy Workers remain recoverable; do not infer their supervisor from
screen coordinates, display names or whichever Helper happens to be MAIN. Offer
owned, read-only legacy history access/recovery through appropriate Helper or
management surfaces without restoring a direct Worker send entry point. Preserve
original files or a recoverable version when conversion is necessary. Existing
promoted Helpers remain Helpers; do not demote or merge their memories.

## Checkpoint sequence and acceptance

1. **Specification and recovery rules.** Record this confirmed model, superseded
   assumptions and preserved local work. Keep original behavior until each tested
   replacement route is ready; do not advertise full completion.
2. **Provider runtime.** Complete installed saved-Agent connection policy and
   native adapters with injected services. Preserve exact source/context,
   credentials, cancellation, stale-result rejection and incumbent lifetime.
   This checkpoint is independent of the supervisory UI design.
3. **Assignments and record migration.** Define explicit Helper/Worker roles and
   supervision references; retain existing identities and recoverable histories.
   Test old projects, promoted Helpers, unassigned Workers, cancelled/failed
   migration and permission changes. Establish the durable state required by
   the next checkpoint without introducing a fixed team system.
4. **Shared Helper shell and request integration.** Install Helper-facing
   conversations/characters, remove direct Worker send/promotion entry points,
   and add workload dots/counts and semantic-object Helper indicators. Route real
   execution through internal Workers while preserving review/cancel/conflict
   behavior. Test Windows, Android adapters and actual Linux UI with providers
   injected; record device/desktop limitations explicitly.
5. **Supervisor persistence and chief journal.** Persist Task/Callback identity,
   recruitment snapshots, overall directives, journal entries, conditional
   authority handoff and temporary-supervisor recovery. Test restart, timeout vs
   disappearance, existing living Workers, stale original commands, journal/save
   failure, duplicate retry and return/handoff. Main Helper must still recruit
   Workers directly as an ordinary Helper.
6. **Chief-only augments.** Implement explicit augment actions and authorization
   in their own scope. Do not silently add instant-table, general render-authoring
   or other later features to the shell/runtime checkpoints.

Each code checkpoint needs small verified ordinary commits/pushes, exact remote
hash/CI evidence, and updated remaining coverage. No force push, committed
images/build products/secrets/conversation logs, real paid inference, external
authentication or production credential writes during verification.

## Assignment schema checkpoint

Add backward-compatible fields to public `Participant` records, retaining all
existing lists/record identities. New saves use collaboration document version 3;
versions 1 and 2 remain readable. Older engines reject version 3 instead of
silently deleting unknown supervision fields on save:

- `AiRole`: `Unspecified` (legacy/default), `Worker`, or `Helper`. Non-AI records
  remain outside this role model. This role labels identity; it grants no Work,
  private-memory, chief-executor or augment permission.
- `SupervisorParticipantId`: a Worker-to-project-Helper participant reference.
  This is distinct from global `HelperId`, private `OwnerId` and provider `AgentId`.
  An empty value means explicitly unassigned. Preserve unresolved references on
  load; a missing supervisor is evidence for recovery, not permission to respawn.
- `SupervisorRevision`: a monotonic revision of this Worker's assignment, used
  for conditional owner-driven assignment changes. This local assignment revision
  does not by itself claim distributed command fencing; durable supervisory
  commands/authority handoff must enforce their own persisted authority epoch in
  the later persistence checkpoint.

Installed CoreTools owns an explicit, cancellable migration action. It classifies
only controlled legacy AI participants: a valid existing global `HelperId` means
Helper (including promoted Helpers); an empty `HelperId` means Worker. Invalid or
contradictory identity data fails before changes rather than guessing or deleting.
Missing local Helper profiles do not demote an existing Helper identity. Migration
adds role data, preserves other participant fields and lists, is repeatable, and restores
roles in memory and persisted state if notification/persistence fails. It neither
opens histories nor rewrites private memory or credentials. Native initialization
invokes this installed action; factory construction itself stays inert.

Initial assignment changes require current Work permission, control of both
participants, explicit Worker/Helper roles and an enabled owned Helper profile.
Changing or clearing assignment must reject stale revisions and running/pending
work; completed historic work and all message/Task identities remain. No implicit
MAIN fallback. Removal of a Helper with explicitly assigned Workers must wait for
an explicit safe reassignment/recovery path rather than leaving those identities
silently orphaned. External disappearance remains representable and recoverable.
Tests must cover old/promoted/missing-profile records, unassigned Workers, malformed
identities, cancellation, post-save failure and rollback, repeated migration,
foreign ownership, permission revocation, stale revisions and active work.

Version 3 also retains detached Helper participant metadata in
`ArchivedParticipants`. These records preserve project participant/owner/source
identity for history recovery; they grant no active participation or execution
permission. Explicit disconnect archives the controlled participant atomically
with removing its active presence. Explicit rejoin restores the latest matching
owned Helper participant ID, leaving other archived identities intact. Identity
collisions fail rather than merging histories. Rollback restores both active and
archived lists. Role migration also classifies owned archived legacy records;
foreign records remain untouched. No private conversation text or credentials are
copied into the archive. Worker records and Tasks are not recreated by rejoin.

Reactivating an archived Helper revalidates its saved permission mask against the
current owner's grants before restoring participation. A historical Apply grant
cannot bypass owner permission revocation. Denial retains the archive for history
recovery and performs no role, presence or private-directory writes.

Invalid migration data is reported by the common workspace notice during native
initialization; unrelated project editing remains available. The explicit
migration action still fails atomically, and malformed identities gain no
supervision authority. A freshly mounted workspace inspects the same public role
records without mutating them, so the recovery notice survives view reentry.

## Helper request routing boundary

The installed factory owns a project-scoped request router, independent of mounted
conversation views. Explicit `Begin(helperParticipantId)` selects only an idle,
owned Worker already assigned to that exact active Helper and source; otherwise
it recruits a new internal Worker with an explicit assignment. It never adopts
an unassigned legacy Worker or another team's Worker implicitly. Recruitment is
persisted before returning a request lease and compensates failed writes. The
lease has a fresh request identity independent of chat/thread IDs. Existing
pending work/checkpoints prevent reuse even if no native runtime is attached.

Lease validation rechecks owner/Helper/Worker Work grants, active record identity,
private Helper/source enablement, source configuration, assignment revision and
session access before connection and dispatch. Native adapters cannot substitute
a selected/default Agent. Cancellation marks the lease cancelled but reserves
its Worker until the operation releases it; a late provider cannot overlap a new
request on that Worker. Disposing a conversation view does not release a request;
disposing the project router cancels all outstanding leases. These are local
execution reservations, not file/object locks or distributed authority fencing.

Private context resolves the explicitly supervising global Helper, retaining
significant global memory plus only this project's memory. Workload projections
come from owned explicit assignments. Routing itself does not read/rewrite old
histories, publish prompts, connect providers, or create chief directives. The
conversation integration must validate the lease again at each asynchronous
adoption/dispatch boundary and release it in `finally`. Durable Task/Callback and
chief-journal persistence remain checkpoint 5; a local lease is not that journal.

Acceptance includes independent concurrent requests, cancellation with delayed
cleanup, reuse after release, pending checkpoints, foreign/disabled/stale records,
source or permissions changing during connection, post-save recruitment failure,
project disposal, and global/project memory isolation. Keep existing Worker UI
until shared Helper request/review/history adapters are installed and verified.

## Shared execution acceptance

The installed execution action consumes a Helper request lease and owns saved
Agent connection, provider lifetime, request state, ordinary-text locality,
private identity/memory binding and Helper-authored result delivery. Native hosts
provide dispatch/input capture, configured provider/OS services and existing
review, pack, image and incident service boundaries. They must not supply a native
Worker-send callback as the implementation of the shared action.

Ordinary text clears global pointing only while preparing the request and restores
it even on failure. Only the explicitly supplied frozen attachment and request
scope are added. The provider receives the private prompt; public Work activity
uses an explicit public task description or neutral activity label, not an automatic
copy of private chat. Existing review constructor behavior remains available to
legacy callers; the shared Helper path chooses the private-safe public projection.

Validate the lease before provider adoption, request preparation and review
completion, immediately before reviewed file application, and before each reviewed
command/retry. Keep cancellation effective through delayed connection and review;
release the Worker only after provider/tool cleanup. Preserve deferred reviews,
incident checkpoints, tool scopes, incoming-change callbacks and local drafts.
Failures return an explicit failed/cancelled/suspended exchange, retaining useful
answer text and error events; they cannot fabricate a successful answer.
Completed results are attributed to the Helper, with Worker/request IDs retained
as internal provenance. Delivery failure must retain the private result for retry.

Memory tools use the explicitly supervising Helper identity. Significant global
memory is the default; explicit project scope stays local. A failed private save
rolls back memory rather than granting a new identity or publishing it. Source
configuration, history consent and blocked-thread changes must invalidate pending
adoption through the captured native session-access predicate. Verification uses
injected providers/credentials and real workspace/review tools, covering plain
text locality, delayed cancel/reentry, provider/review failure, permission/source
changes, memory isolation/save failure and Helper result authorship before native
conversation routes switch to this runtime.

## Shared conversation acceptance before native replacement

A project-scoped installed conversation controller owns Helper timelines,
composer drafts, request submission/cancellation, navigation, read receipts,
private-history filtering and persistence payloads. Mounted views subscribe to
that controller. Closing a character/view hides it without disposing the project
runtime or cancelling active work; reopening retains the selected exchange and
unsent text. Project disposal cancels execution and releases view subscriptions.

Use the same installed view definition for all native hosts. Keep the existing
question/answer bubbles, history navigation/dots, character/placement, explicit
Yogi attachment, transcript access and cancel controls. Rendering, pointer/key/
touch events, window placement, native clipboard/file pickers and private file
I/O are adapter boundaries. Public project/human conversations and incident/review
surfaces remain available. Workload counts/dots belong to the supervising Helper;
internal Workers are never direct-send or promotion launch targets in the new UI.

New private history must use a versioned Helper/project/owner identity envelope
in a separate common file. Existing Windows/Android Worker and promoted-Helper
histories remain readable recovery sources and are not overwritten, deleted or
automatically imported into an unrelated Helper. No history read/write occurs
for a foreign owner or while local history consent is disabled. Blocked threads
must not reappear in view/context or be restored by a pending completion. Unknown
versions, corrupt data and failed writes produce explicit recovery state; retain
new in-memory results and offer retry without overwriting the unreadable source.
Private storage paths and OS reads/writes come from the native boundary; identity,
version validation and serialized content belong to the installed controller.

Acknowledgement applies only to the currently displayed exchange when native
input/visibility confirms it was viewed; a background render or opening the
sidebar cannot mark the entire history read. Preserve a historical selection when
new exchanges arrive. Foreign Helpers show only authorized public messages and
neutral/public character assets, never a local private profile matched by ID.

Acceptance must exercise real installed actions with injected execution and
storage: concurrent requests, cancellation during connection/review, close/reentry
while working, failed provider/history save, retry, blocked-thread/consent changes,
foreign ownership, malformed/unknown history, receipt isolation, explicit Yogi
scope, private global/project memory, and native Linux input against the real
shared execution path. No route may claim completion from a fabricated answer or
from calling an old native Worker-send implementation.
