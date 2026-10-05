# Installed public project and Room conversation

This stage implements FINAL_UI_CONTEXT.md's project conversation alongside the
already verified global/private Helper conversation and read-only legacy recovery.
The selected project owns public dialogue. Human users and Main Helper receive new
project dialogue continuously; ordinary Helpers receive explicit tags. Internal
Workers remain managed by Helpers. Shared definitions and authoritative actions
must come from the verified installed CoreTools factory on every platform.

## Ownership and lifetime

One controller belongs to the exact selected session, installed role actions,
current ProjectStudio object and device AI directory. Its lifetime is independent
of visible conversation panels. Opening or reopening a panel preserves pending
requests and never replays old messages, creates a project or requests a provider.
Selecting another project disposes the former controller, cancels its requests and
rejects late provider/results before any delivery into either project.

New public project messages reach the configured Main Helper. Explicit ordinary
Helper tags resolve exact participating identities and deduplicate Main mentions.
Main's own replies do not reenter its request queue. The existing public AI-depth
limit remains authoritative. Historical messages are context, not executable
requests. Foreign-owner mentions remain public notices for the owner's connection;
this device must not open foreign private credentials or provider state.

Failed message persistence removes the staged in-memory message before returning
an error, so correcting storage and retrying cannot publish a phantom duplicate.
Installed display-observer failures are reported separately from accepted posts.

Each Helper has a serialized public request queue. Pending operations have stable
IDs, readable state, cancellation and retry after failure. Retry never reposts the
human message. Cancellation accepts neither a late provider nor a late response.
Role, owner, source identity/configuration and project consent are checked before
connection and again before delivery. A provider must be fresh for the public
request. No private Helper memory, experience, Worker transcript or ambient editor
pointing enters public context. Existing PublicConversationAccess forbids private
files and task-control tools. Full Helper task execution continues through the
existing private installed router, internal Worker and review/clash boundaries.

## Shared panel and messages

The installed conversation view owns transcript, composer, explicit send/cancel/
retry actions, public Yogi attachment snapshots and project chat/log selection.
Native adapters supply render/input/clipboard and explicit OS/semantic Room entry
boundaries. Project chat is a wide lower panel, with a log tab and the latest
meaningful log line. The common panel uses public message snapshots and exact
source identities, not native workflow handlers. Native OS layout adapters place
it beside the shared workspace; they do not reinterpret routing policy.

Closing a panel preserves requests and its unsent in-memory composer. Composer
text and attachment remain local until explicit Send; panels for separate Rooms
have independent buffers. Public Yogi delivery is an explicit send of a
frozen copy; dragging an attachment into a composer does not send it. Inspecting a
received box uses the installed inspector. Esc/whole-draft close affect only the
current temporary composition. Room dialogue keeps existing semantic EnterRoom,
public-only context and parent/channel boundaries. Private recovery is excluded
from public capture. Read receipts acknowledge only exact displayed permitted
messages, never background or hidden history.

## Acceptance before retirement

Verify the installed controller/view on Windows, Android and Linux renderers:
no requests on mount; historical message non-replay; continuous Main routing;
ordinary explicit tag only; deduplication; foreign owner/no private connection;
public context/task-tool exclusion; queue ordering; close/reentry; cancel during
connect/reply; late-provider disposal; failure and retry without duplicate post;
source/role/consent revocation; project detach; public/Room boundary; immutable
Yogi drop/no-send; displayed receipt locality; shared chat/log controls.

Exercise production SDL input and the Windows native fixture with injected
services. Android official-reference compilation and actual APK CI are distinct
from physical-device execution. Never use paid requests, actual authentication or
real credential saving in verification. Only after recovery and Helper replacement
are verified may direct Worker chat, promotion and creation UI routes be retired;
participant storage, internal Workers, pending edits and clash callbacks remain.

## Bounded transcript viewport acceptance

The installed view declares a fixed-height vertical `editor.viewport` children
slot for transcript and operation rows. Composer, send, tabs and latest-log stay
outside this viewport. Windows/Android native scrolling and Linux clipped drawing,
wheel and hit testing interpret this identical declaration. Offscreen rows have no
hit target or read receipt. Updating keyed children preserves the native scroll
position. Earlier public records remain reachable through an explicit installed
"earlier messages" action, without replaying model requests. Viewport scrolling
never sends or changes semantic Room activity. This is a standard native layout
adapter capability, not a new authoring workflow.
