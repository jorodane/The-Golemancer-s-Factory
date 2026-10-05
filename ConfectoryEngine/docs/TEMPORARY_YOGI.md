# Temporary Yogi composition and attachment inspection

This implements the finalized Yogi portion of FINAL_UI_CONTEXT.md and the
project-optional conversation contract. Installed CoreTools owns the draft,
validation, inspection/composition definitions and all shell actions. Hosts supply
explicit capture/reference discovery, navigation, image display, OS drag/drop,
input, surface placement and current project identity. No hidden project is opened.

There is one application-owned, in-memory draft. It is not a collaboration vault
record. Collecting the first item opens it; collecting a new item into a sealed
box unseals that same draft and keeps prior items. Collection validates an entire
candidate before changing the draft. Title and explanation are bounded. Item ×
removes only that item; whole-box × and Esc empty and close the draft. Leaving or
switching an active project clears it. Delivered/history attachments and existing
legacy stored bytes are never deleted by draft actions.

Seal validates content and collapses the composition view into a draggable parcel.
Double-click unseals it. Drag/drop obtains a validated sealed copy. Empty, malformed
or stale deliveries produce a visible error and do not terminate the editor.
The box name and item count are separate. No vault/add-box management menu remains
in the ordinary sidebar once native draft replacement is verified.

Inspection uses an immutable validated snapshot with explanation, EY navigation,
LaY previews and full-image display. A reference in another/unopened project is
identified as such and cannot silently open a project. Navigation revalidates the
current source; deleted/closed targets remain visible with a clear reason. Editing
an inspected attachment creates a new unsealed draft identity and preserves the
original attachment. It works with no project selected. Invalid historical content
opens a readable error state with a close action, not a crashed native window.

Acceptance: identical mounted actions for all platform catalogues; atomic failure,
duplicate capture, seal/unseal/new-item transitions, item/whole removal, invalid
empty delivery, copy isolation, project exit, stale source navigation and no-project
inspection/edit-copy. Native checks cover actual pointer/key input, drag payload
freezing and reentry; screenshot/credential boundaries remain in capture adapters.
No paid inference, external authentication or real credential writes in tests.
