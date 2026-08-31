# Eyu

> *Eyu (이유) — Korean for "reason" or "why". Pronounced roughly "eh-yoo".*

> A source-agnostic ontology inference engine: turns declared structure and raw
> records into proposed entities, relations, and grounded claims — the reason
> a piece of data is shaped the way it is, made explicit and citable.

**Status: pre-implementation.** This README fixes the contract; the code follows.

---

## Why

Every system that wants an ontology chatbot ends up rebuilding the same brain:
infer entities and relations from structure, decide how confident that
inference is, and refuse to answer without a citable path back to the source.
That logic has nothing to do with *where* the data lives — whether it's a raw
document store you own, a schema you declared elsewhere, or a table in someone
else's database you can only read. Building it once, separately from any
storage or access model, is the only way it doesn't get rebuilt every time a
new consumer needs it.

## The idea

```
[structure hints]  ──┐
                      ├─▶   Eyu   ─▶  proposed entities / relations
[raw records]      ──┘              + confidence
                                     + {claim, sources[], path[]}
```

- **Input, not fetch.** Eyu never pulls data. A caller hands it declared
  structure (field hints, a schema, an M3L-style declaration) and/or raw
  records to look at. What the caller doesn't supply, Eyu doesn't know.
- **Declared always wins.** Where structure is explicitly declared, the
  declaration is the answer. Inference only fills what nothing declared.
- **Confidence routes, it doesn't decide.** Every proposal carries a
  confidence score; a caller-defined threshold routes it to auto-apply,
  human review, or draft-only. Eyu proposes — it never applies anything.
- **No claim without a reason.** A response is a structure of
  `{claim, sources[], path[]}`. A claim that can't cite its sources cannot be
  expressed — this is enforced by the output shape, not by a prompt.

## What Eyu is

- **A judgment library.** Given structure and records, it proposes what the
  entities, relations, and their confidence are — including which records
  refer to the same real-world entity (entity resolution is part of
  proposing what the entities *are*, not a separate concern). That's the
  whole surface.
- **Storage-agnostic.** It has no raw store, no projection target, no query
  engine of its own.
- **Provider-agnostic.** Model access is a single injected port; local or
  hosted inference both work unmodified.

## What Eyu is not

- **Not a store.** It doesn't own raw data, projected tables, or a graph
  database. Something upstream owns storage; Eyu only judges what's in it.
- **Not a permission system.** It has no concept of who is allowed to see
  what — that's a consuming system's job, enforced before or after Eyu is
  called, never inside it.
- **Not a federation layer.** It doesn't know how to reach a remote system,
  retry a query, or merge live records. It receives records; it doesn't fetch
  them.
- **Not an agent, not a UI, not a chat surface.** It answers a structural
  question with a grounded proposal — nothing about how that proposal reaches
  a person is in scope here.

## Ports

| Port                | Responsibility                                                        |
| -------------------- | ---------------------------------------------------------------------- |
| `IStructureSource`   | What the caller has already declared — field hints, relations, version |
| `IRecordSample`      | Raw records to infer from, when declaration alone is insufficient      |
| `IOntologyProposer`  | The core judgment: entities, relations, confidence, and entity resolution (merging records that denote the same entity) — all from the above |
| `IGroundingContract` | `{claim, sources[], path[]}` — the shape every answer is expressed in  |
| `IModelClient`       | Provider-neutral inference access (local or hosted)                    |

Each consumer implements `IStructureSource`/`IRecordSample` for its own world
— an owned raw store, a declared schema, a federated read — and gets the same
proposal logic back through `IOntologyProposer`.

## License

[MIT](LICENSE)
