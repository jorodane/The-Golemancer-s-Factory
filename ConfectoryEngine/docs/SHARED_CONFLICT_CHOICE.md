# Common request-local conflict choice

Installed CoreTools owns the conflict comparison definition, local candidate
selection, permission checks, exact returned candidate identity and cancellation.
The existing CollaborationReviewCoordinator remains responsible for opening clash
records, comparing current document/proposal versions, recomparing stale decisions,
merging nonconflicting regions and recording the resolution. No choice applies files
or creates an edit lock. Existing Windows/Android conflict and AI-adjudication
features remain available while Linux adopts the common choice.

The choice receives an explicitly selected collaboration workspace, its live open
conflict and bounded candidate metadata/text. It snapshots displayed content and
cannot accept a candidate changed after display. Human Apply permission and open
conflict identity are checked at confirmation. No default candidate is silently
chosen. Failure stays visible and local; cancel disposes only the pending choice.
Token cancellation wins over a late click. Closing or unmounting a pending choice
cancels it; disposing an accepted choice retains its exact returned identity.

The view presents target, baseline, author/intent, candidate text, local selection,
confirmation, cancellation and errors with native readonly copy/scroll support.
The coordinator rechecks original/proposal versions after the asynchronous choice,
so no stale dialog can authorize a changed target. A Linux native UI dispatch
context preserves existing coordination callbacks after awaits.

Acceptance: identical installed views on all three platforms; no-premature-write,
explicit candidate choice, permission loss/recovery, changed candidate/open-conflict
identity, token cancel/late click, dispose/reentry; actual Linux overlapping human
and Helper proposals, original changes while deciding, preserved nonconflicting
regions, incoming-change callbacks and selective application through the existing
review boundary. Tests use injected providers without paid/auth/credential actions.
