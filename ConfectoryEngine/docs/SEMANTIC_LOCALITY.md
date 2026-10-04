# Semantic ownership and lazy queries

`EditorSession.Registry` reads pack manifests and ownership declarations. It does
not parse their Data/Ui content or read implementation bodies. Source listings
resolve only declared project/Compile metadata; their cache fingerprint includes
project bytes and source filenames so additions and removals are visible.

`EditorSession.SemanticIndex(pack)` owns document shards. `Document(path)` rejects
foreign ownership, verifies the current content hash and reuses unchanged shards.
`InvalidateDocuments(paths)` evicts only affected shards and locator entries;
accessing the compatibility `Index` explicitly requests a whole-workspace index.
An explicit `Refresh` remains a broad metadata/cache refresh.

Packs can publish stable identities without exposing details:

```xml
<Data path="objects.xml" role="concept-objects" />
<Export key="concept-object:potion" document="objects.xml" />
```

Roles also include `concept-schema` and `concept-views`. Exports must reference a
declared document. The locator returns only key, pack and document. Legacy packs
without exports use a lazy streaming identity bootstrap. This compatibility path
can read unrelated document bytes on its first lookup; it is measured separately
as `LocatorDocumentsRead`, not represented as a zero-read query.

Pack-scoped `ListObjects` enters that pack's document shards. `ReadElement` no
longer collects observed strings from the project. The optional
`IEditorProjectObservedValues.GetObservedValues(pack, kind, element, field)`
queries one owner explicitly, respects draft snapshots, caches parsed documents,
and is available through both in-process and worker-pipe adapters. Observations
are suggestions, not declared enum or reference constraints.

Asset lists cache declared paths per pack. Manifest-only assets need no semantic
XML reads. Legacy schema Asset rules inspect only the requested owner's declared
XML; their fingerprint includes the schema and those documents. Bitmap bytes
are read only by `ReadAsset`, retaining the existing size and invocation budgets.

`LocalityCounters` separates manifest reads, metadata queries, legacy locator
reads, semantic parsing, packs entered, indexes opened/invalidated, document hash reads, global
rebuilds and document writes. Tests compare small and large synthetic projects.
Counters describe actual operations; a cache hit does not count as a parse.

The compatibility global index and whole-project validation remain explicit
operations. Pack movement, dependency graph changes, release validation and
whole-project browsing may legitimately span packs. Single-owner APIs must not
construct the global index before filtering it.

## ConceptSpace snapshots

`ConceptSpace.Open(session)` shares that session's registry, locator and counters.
Opening reads registration metadata only. `Concept(id)`, `Object(id)`, `View(id)`
and the category/function equivalents use stable identities to enter an owner's
layer. `Objects.InPack(pack)` and `Rows(concept, pack)` do not enumerate foreign
collections. Iterating a compatibility collection explicitly composes that layer
across packs; maps, inverse-reference discovery and unfiltered lists are global
queries, not prerequisites for opening one object.

Editable snapshots remain pinned until saved or reopened. Saving checks every
actually observed document hash, including observed foreign contracts, and all
proposed paths against dirty buffers. Unopened semantic documents are not pinned
merely by opening a project. A saved move rehomes the same element instances to
its new owner snapshot; failure restores ownership through the existing rollback.

A pure View update validates its View layer and writes that document only. Other
local edits validate the changed owner's layers and enter referenced contracts by
ID. Dependency validation uses unchanged packs' manifest edges. Explicit global
validation and structural moves still validate the whole requested graph.

Saving a pack adds semantic Data roles and complete stable-ID exports. Existing
central ConceptSpace registrations continue to load and round-trip; self-registered
and newly created packs need no central per-document registration. Formatting-only
metadata differences never produce writes. Changed document shards are invalidated
individually; registration/source mapping changes reload metadata. Unchanged
schema/object/view bytes and observed conflict protection are retained.

Hash conflict checks also read already observed contracts. They are counted as
`DocumentsHashed` and semantic owners in `PacksRead`; they are not semantic parses.
A user who explicitly opened a global collection pins those observed documents.

## Shared native editor controller

Windows and Android retain their native controls, popup placement, hover/Shift
and touch/long-press input. `ConceptEditorController` owns creation, save/move and
rollback, schema/function edits, field modes, reference/function choices, default
View bindings, table/card fallback and source-pack columns. `ConceptMapState`
owns Variation history and reference selection. Native adapters call these rules;
they do not independently construct semantic creation or migration transactions.
Public schema/function contract changes still trigger dependency validation;
ordinary label/value/View edits use the owner path. Function source bodies are
validated only for the changed owners.
