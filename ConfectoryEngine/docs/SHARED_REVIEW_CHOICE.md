# Shared request-local review choice

The installed CoreTools factory owns review selection and its view. Windows,
Android and Linux mount the same elements; hosts retain OS presentation, dispatch,
existing collaboration/clash callbacks, reviewed application and build retry.
This component does not introduce a lock, resume a request, invoke AI or apply files.

A mounted choice exposes all groups and pending items, per-file and selectable
hunk selection, changed excerpts, expandable surrounding context, full before/after
text, image previews, queued command details, and selected counts. Compound items
remain indivisible. Selection starts with all items; deselecting all permits an
explicit finish without changes. Cancel/close/dispose cancels the decision and
request-local proposal, never applies it. Already approved decisions are not
cancelled by unmounting. A cancelled token wins over a late confirmation.

Recomparison invokes the existing host collaboration preparation callback, disables
selection while it runs, and resets selection to the newly compared proposal.
Preparation failure stays visible and retryable. Acceptance rechecks human Apply
permission, current proposal identity/content and batch validation. Changed
proposals require a fresh comparison. Selecting hunks only stages the selected
proposal; the host must still call the existing reviewed application boundary.
No unconfirmed content is copied to a shared document.

Acceptance coverage must exercise identical mounted actions on all platform
catalogues, groups/hunks/compound items, full/context/image previews, empty finish,
permission loss, stale proposal, preparation failure/retry, cancellation during
preparation, late completion, disposal and reentry. Actual native rendering/input
and production mounting remain separately reported from portable action tests.
