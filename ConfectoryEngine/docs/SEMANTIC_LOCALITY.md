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
reads, semantic parsing, packs entered, indexes opened/invalidated, global
rebuilds and document writes. Tests compare small and large synthetic projects.
Counters describe actual operations; a cache hit does not count as a parse.

The compatibility global index and whole-project validation remain explicit
operations. Pack movement, dependency graph changes, release validation and
whole-project browsing may legitimately span packs. Single-owner APIs must not
construct the global index before filtering it.
