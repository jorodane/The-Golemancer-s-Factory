# Helper-facing supervision contract

Status: user-confirmed replacement direction, 2026-10-04. This supersedes the
Worker-direct-chat and Worker-to-Helper promotion direction in earlier shell
handoffs. It defines acceptance and sequencing; it does not claim that the
existing runtime already implements the replacement.

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
