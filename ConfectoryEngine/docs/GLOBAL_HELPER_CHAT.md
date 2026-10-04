# Global Helper chat lifecycle

Implementation contract for the next shell stage, derived from the accepted
`FINAL_UI_CONTEXT.md`. The published project execution/timeline foundation remains
an internal Worker capability; it does not establish this lifecycle by itself.

## Ownership and lifetime

- The installed CoreTools factory owns the application-level Helper conversation
  controller. Opening a Helper resolves the exact device-owned Helper identity;
  it never creates, opens, switches or infers a project, and never calls a provider.
- Conversation views subscribe to the controller. Closing or remounting a view
  preserves drafts and running requests. Explicit cancellation targets one request.
- A Helper's project-free provider conversation accepts one request through
  cleanup, preserving an attempted next draft. Other Helpers and separately owned
  project Workers remain independent; this is not a document edit lock.
- With no current project, an explicit send uses the Helper's saved Agent source,
  global private memory and a project-free context. No EditorSession, project
  manifest, collaboration workspace, Worker or synthetic project is created.
  Provider-private transport storage is not a project and contains no project files.
- An explicitly opened project may be bound to the controller. Binding itself is
  inert. A project request captures that binding, exact Helper participation and
  the installed project execution action before awaiting any external service.
  Worker execution remains bound to Helper, Task/request and Project.
- Project exit detaches the binding and cancels its project-owned execution;
  it does not close global Helper conversations or erase global memory/history.
  Late provider completion must not adopt a new project or publish into it.
  Native owners retain the bound workspace/execution services until detaching them;
  bindings require the exact session, identity directory and installed generation.

## Context and service boundaries

- Global requests expose only the installed global-memory tool. Project read,
  inspect, edit, command, image and review tools are unavailable without a captured
  authorized project request. Tool calls recheck source, Helper, consent and request
  lifetime. Unknown tools fail explicitly.
- Project execution continues to use the existing guarded bridge, review and clash
  callbacks. No new edit lock, durable Supervisor journal or implicit confirmation
  is introduced. Native review/incident services must survive production adoption.
- Each history turn records its source project identity, or an empty identity for
  global chat. The UI may show the owner's history across contexts; model context
  includes only global turns and the current captured project's turns. Global
  memory carries concise explicitly retained significant facts across projects.
- History consent and blocked-thread rules apply before reads, writes and model
  context construction. Native services provide private storage with
  compare-before-write; CoreTools owns envelope validation, filtering and recovery.
  Each request captures retention consent. A history-disabled request can display
  its local answer, but cannot forward earlier prompts or become archived later
  merely because the user exits the project or enables history. Explicit Helper
  memory remains a separate opt-in action. Global character placement uses the
  same pack geometry rules as project participants and private history storage;
  disabling history also disables these placement writes.
  Old project/Worker histories remain available through explicit readonly recovery.
- Native adapters supply rendering/input, dispatch, credentials, provider transport,
  current consent/settings and storage. They do not choose fallback Agents, execute
  conversation actions or manufacture project lifecycle transitions.
- Project execution captures whether the platform can execute project commands.
  The installed pack omits unavailable build/project tools and rejects direct
  calls, including editor-pack build, before staging or executing a command.
  Android retains document editing, review, peer handoff and incident callbacks;
  adopting the common runtime must not grant desktop execution capabilities.

## Required acceptance

1. Open, close and reopen a Helper with zero projects: no project lifecycle calls,
   provider calls or credential writes; the same unsent input remains available.
2. Explicit global send reaches an injected real provider interface with empty
   project scope, global memory and no project/file/command capabilities. Failure,
   source removal, setting revocation, cancel and late connection dispose safely.
3. Open a real explicitly selected project, send, then exit or switch while waiting:
   the captured request cannot drift, publish or apply review into the new project.
   A later global request works without reopening the previous project.
4. Same Helper across projects: prior project-local prompts/memory never become
   another project's model context; explicit global memory remains available.
5. Mounted views use the same installed definition on Windows, Android and Linux.
   Native verification covers send, close/reentry and cancel using injected services;
   physical-device, external-auth and paid-model limitations are reported separately.
6. Production adoption preserves review, incident interruption/checkpoints, incoming
   clash callbacks and delivered Yogi attachments. Worker-direct/promotion routes
   are removed only with tested replacements and readable legacy history.

Current implementation: CoreTools supplies `GlobalHelperExecution`, and the same
timeline/view support a null project/collaboration context. Project work delegates
to the existing installed project runtime after an explicit binding. Portable
verification covers the lifecycle and boundaries above; native fixtures are being
verified. The Windows adapter now mounts the common floating view for existing
Helper entry points, binds explicitly selected projects, registers pending reviews
before provider tools run, and mirrors cancellation into existing incident hooks.
The global adapter also supports a genuine null-session mount; home sidebar access
still requires the forthcoming common entry-point update. Android now mounts the
same view through its existing Helper entry points, with shared placement/input,
document-only capabilities and the existing review/peer-handoff/incident services.
Its global controller skips the legacy standalone workspace binding and never
opens a project for chat. Linux production adoption, common global sidebar entry,
legacy recovery and Worker-direct/promotion removal remain pending.
Do not describe the shell lifecycle as complete yet.
