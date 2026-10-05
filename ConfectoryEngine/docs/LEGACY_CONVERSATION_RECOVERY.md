# Read-only legacy conversation recovery

This is the prerequisite for retiring Worker-direct send/promotion/creation shell
routes under HELPER_SUPERVISION.md and FINAL_UI_CONTEXT.md. Existing active and
archived AI participant IDs, original private files, promoted-Helper provenance,
pending edits, Tasks, incidents and review records stay intact. Viewing a history
cannot allocate a Worker, infer a supervisor/source, join a participant, connect a
provider, send a message, acknowledge unseen messages, rewrite history or apply a
proposal. Internal Helper request execution continues through the installed router.

Installed CoreTools owns record selection, ownership/consent checks, parsing,
blocked-thread filtering, immutable snapshot presentation, selection/reentry,
readable missing/corrupt/oversized notices and Yogi inspection. Native services
supply read-only file bytes from the captured selected project's private state and
native rendering/clipboard/image/folder boundaries. A participant's ID is not an
arbitrary filesystem path. Foreign/private records are rejected before file access;
archived records require exact human ownership without reactivation. Live control
and history consent are rechecked when viewing/selecting/inspecting a record.

Supported original representations are Windows `participants/<id>/turns.json`,
Android `exchanges-mobile.json`, and the Android `turns-mobile.json` role/text
fallback when exchanges are absent, empty or malformed. Provider transport files are never imported.
Original bytes remain unchanged. Missing or malformed data remains recoverable via
its original location; no conversion overwrites the originals. Historical working
states are labeled as interrupted/recovery context without modifying their stored
state. Delivered Yogi records are inspected as immutable copies and do not become
an executable proposal. No automatic promotion or source fallback is inferred.

Acceptance: identical installed recovery views/actions on Windows/Android/Linux;
active/archived ownership; missing source/assignment; missing, corrupt and oversized
files; consent/blocked-thread filtering and revocation; selection/cancel/reentry;
immutable Yogi inspection; exact original-file preservation; no private foreign
read, execution, writes, receipt changes, supervision mutation or provider calls.
Only after this recovery path and Helper replacements are verified may native
Worker-direct and promotion/creation entry points be retired. Low-level participant
and internal Worker services remain available to their authorized owners.
