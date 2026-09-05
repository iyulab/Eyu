# Eyu

> *Eyu (이유) — Korean for "reason" or "why". Pronounced roughly "eh-yoo".*

> A source-agnostic ontology inference engine: turns declared structure and raw
> records into proposed entities, relations, and grounded claims — the reason
> a piece of data is shaped the way it is, made explicit and citable.

**Status: contract implemented; grounding-integrity and response-parsing reliability validated
against a real model — entity-resolution accuracy is not measured, not yet exercised by an
external package consumer.** All five ports exist as C# types (`Eyu.Core`), plus a first source
adapter (`Eyu.Formbase`). `IOntologyProposer`'s judgment logic (`SinglePassOntologyProposer` +
`HttpModelClient`) is implemented and has been measured against a real model (GPUStack
`qwen3.8-27b`) across two domains — every cited source id existed among the records given, 0
violations both times. That check alone does not prove a citation actually backs its claim, so
the same measurement now also runs a mechanical content-overlap check
([`GroundingOverlapCheck`](src/Eyu.Core/Grounding/GroundingOverlapCheck.cs)) between each claim
and the record content it cites — a heuristic signal to spot-check per run, not a confirmed
defect count (see the type's doc comment for why token overlap is not a semantic verifier; a
run's own counts are in its report, not restated here since the pre-filter below changes them
run to run). `SinglePassOntologyProposer` also takes an optional `LinkageOptions`
(`Eyu.Core.Linkage`) that runs a Fellegi-Sunter record-linkage pre-filter before the model call:
record pairs it confirms as a match are injected into the prompt as "merge them, do not
re-decide", gray-zone pairs are injected as a log-odds hint for the model to judge itself, and
confirmed non-matches get neither — so the prompt does now carry a resolution instruction, where
before it carried none. A live run measured that this classification is observable (per-pair
Match/GrayZone/NonMatch, EM convergence status, match prior) and that changing the thresholds
measurably changes both the resulting prompt and the grounding-overlap counts. What that run does
**not** establish: whether any particular threshold setting is more *correct* — no labeled
ground truth exists to score the pre-filter's own match/non-match calls against, so
entity-resolution accuracy remains unmeasured — or whether the proposer's self-reported
confidence is informative on its own, which it was not in an earlier measurement (see
[design rationale, §D](docs/philosophy.md)); gray-zone cases now combine it with the
Fellegi-Sunter prior via a Bayesian update instead of using it alone. Field comparison inside
that pre-filter is exact-match (case/whitespace-insensitive) by default; `LinkageOptions.
UseStringSimilarityComparator` opts into a Jaro-Winkler threshold instead, so two records
denoting the same entity but differing only in notation (punctuation, spacing) still register as
agreeing on that field — still no ground truth to say which mode classifies better on any given
dataset, so the option exists but the default is unchanged.
`Eyu.Core` is not yet published, so today the wiring is proven by in-repo mock-backed tests
rather than by a consumer referencing the package the way the
[coupling smoke test](tests/Eyu.IntegrationSmoke.Tests/SchemaProposerCouplingSmokeTests.cs)
already does for `Formbase.Core`.

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

This is the bet the design makes, not yet a cross-validated claim: today Eyu
has exactly one consumer (`Eyu.Formbase`, in this same repo), and no external
package consumer has exercised it (see Status above). Read "every system" as
the target the architecture is built toward, not as a track record.

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
  Self-reported confidence is not trustworthy uncalibrated — see
  [design rationale, §D](docs/philosophy.md).
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
| `IOntologyProposer`  | The core judgment: entities, relations, confidence, and entity resolution (merging records that denote the same entity) — all from the above. Each proposal also carries a `VocabularyOrigin` (`Innate` \| `Acquired`) — see [design rationale, §C](docs/philosophy.md) |
| `IGroundingContract` | `{claim, sources[], path[]}` — the shape every answer is expressed in  |
| `IModelClient`       | Provider-neutral inference access (local or hosted)                    |

Each consumer implements `IStructureSource`/`IRecordSample` for its own world
— an owned raw store, a declared schema, a federated read — and gets the same
proposal logic back through `IOntologyProposer`.

## Further reading

[Design rationale](docs/philosophy.md) — why judgment requires structure
first, what kind of thing a proposed ontology is, and why proposals carry an
innate/acquired origin tag, each with a confidence grade on how
well-anchored the reasoning is.

## License

[MIT](LICENSE)
