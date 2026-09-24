# Eyu

> *Eyu (이유) — Korean for "reason" or "why". Pronounced roughly "eh-yoo".*

> A source-agnostic ontology inference engine: turns declared structure and raw
> records into proposed entities, relations, and grounded claims — the reason
> a piece of data is shaped the way it is, made explicit and citable.

**Status: contract implemented; grounding-integrity and response-parsing reliability validated
against a real model; entity-resolution accuracy measured on a labeled public benchmark (B-cubed F1
0.99–1.00 on Febrl synthetic person records) and not yet on any other data; exercised by one external
package consumer on a Korean-language document corpus, whose first measurement is what the
entity `Name`, per-element `Rejections` and stamped `VocabularyOrigin` answer.** All five ports exist as C# types (`Eyu.Core`), plus a first source
adapter (`Eyu.Formbase`). `IOntologyProposer`'s judgment logic (`SinglePassOntologyProposer` +
`HttpModelClient`) is implemented and has been measured against a real model (GPUStack
`qwen3.8-27b`) across two record domains, and over two Korean company-profile documents passed as
chunks — every cited source id existed among the records given, 0 violations every time. That check alone does not prove a citation actually backs its claim, so
the same measurement now also runs a mechanical content-overlap check
([`GroundingOverlapCheck`](src/Eyu.Core/Grounding/GroundingOverlapCheck.cs)) between each claim
and the record content it cites — text in any script, with Chinese, Japanese and Korean
compared as overlapping character pairs so a Korean claim still matches the record it restates
when the particles differ — a heuristic signal to spot-check per run, not a confirmed
defect count (see the type's doc comment for why token overlap is not a semantic verifier; a
run's own counts are in its report, not restated here since the pre-filter below changes them
run to run). `SinglePassOntologyProposer` also takes an optional `LinkageOptions`
(`Eyu.Core.Linkage`) that runs a Fellegi-Sunter record-linkage pre-filter before the model call:
record pairs it confirms as a match are injected into the prompt as "merge them, do not
re-decide", gray-zone pairs are injected as a log-odds hint for the model to judge itself, and
confirmed non-matches get neither — so the prompt does now carry a resolution instruction, where
before it carried none. The linkage evidence then adjusts an entity's confidence only through the
records the entity claims denote it (`DenotedBy`), never through records that merely mention it:
a machine several work orders name is not penalized because the work orders are different orders. A live run measured that this classification is observable (per-pair
Match/GrayZone/NonMatch, EM convergence status, match prior) and that changing the thresholds
measurably changes both the resulting prompt and the grounding-overlap counts. What that run does
**not** establish: whether any particular threshold setting is more *correct* — no labeled
ground truth exists in these domains to score the pre-filter's own match/non-match calls against
(on a labeled public benchmark it has been scored — [docs/linkage-benchmark.md](docs/linkage-benchmark.md):
B-cubed F1 0.99–1.00 on Febrl person records, 0.990–0.994 on names and addresses alone with exact
comparison); what the pre-filter does report is an
*unlabeled estimate* of its own error rates (`LinkageAnalysis.ErrorRates`: the false-match and
false-non-match rates the fitted EM mixture expects of its own calls, with the preconditions of
that estimate flagged when they did not hold — an estimate is not a measurement, and it says
whether a clerical review of a sample is worth running, not what it would find; the unit such a
review will be scored on is already fixed as B-cubed per record, not per pair,
`ClusteringMetrics.BCubed`, and the sampling and adjudication design for that review is
[docs/clerical-review.md](docs/clerical-review.md), whose estimator is in the library as
`ClericalReviewEstimator`, and the path from a reviewer's verdicts to the per-record scores it
consumes is `ClericalReviewScoring` — the arithmetic that turns a reviewed sample into a
population score with an interval exists end to end and has been run with a benchmark's labels as
the reviewer, which showed a pilot's interval covering far less than 95% where errors are rare;
what is still missing is a review by people on a corpus of the domains above) — or whether
the proposer's self-reported
confidence is informative on its own, which it was not in an earlier measurement (see
[design rationale, §D](docs/philosophy.md)); gray-zone cases now combine it with the
Fellegi-Sunter prior via a Bayesian update instead of using it alone. Field comparison inside
that pre-filter is exact-match (case/whitespace-insensitive) by default; `LinkageOptions.
UseStringSimilarityComparator` opts into a Jaro-Winkler threshold instead, so two records
denoting the same entity but differing only in notation (punctuation, spacing) still register as
agreeing on that field — still no ground truth to say which mode classifies better on any given
dataset, so the option exists but the default is unchanged.
`Eyu.Core` and `Eyu.Formbase` are published on NuGet, and the package is referenced by the
external consumer named above; inside this repository the
[coupling smoke test](tests/Eyu.IntegrationSmoke.Tests/SchemaProposerCouplingSmokeTests.cs)
references `Formbase.Core` the same way, as a package.

---

## Install

Current release: **0.3.0**. Both packages ship from this repository and move together:

```bash
dotnet add package Eyu.Core --version 0.3.0
dotnet add package Eyu.Formbase --version 0.3.0
```

`Eyu.Core` alone is enough to implement the five ports against your own source; `Eyu.Formbase` is
the adapter for one of them and reads `Formbase.Core` as a package, so it pairs with a Formbase
release — this one is built against `Formbase.* 0.10.1`.

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
has one source adapter (`Eyu.Formbase`, in this same repo) and one external
package consumer, on one document corpus (see Status above). Read "every
system" as the target the architecture is built toward, not as a track record.

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
  A call takes one declaration per subject, so a caller whose records name
  several kinds of thing — chunks of a document name companies, people and
  products at once — declares each kind, by name alone when that is all it
  knows (`new DeclaredStructure(SubjectRef.Create("Organization"), [], [])`),
  and the roles it needs kept apart as typed relations (`PartnerOf`,
  `CustomerOf`). A declaration is neither a filter nor a renaming: a type the
  model proposes that nothing declares still comes back, as inferred, under the
  name the model used. Measured on two Korean company-profile documents, three
  attempts each, declaring `Organization`, `Person` and `Product` with four
  typed relations took the entity names proposed under more than one type
  (`company` in one attempt, `organization` in the next) from 7 of 9 to 0 of 9
  in a one-chunk document, and to 0 of 19 in a seven-chunk one that drifted
  little without it (1 of 18), while kinds nothing declared — locations,
  services, dates — kept coming. Two earlier wordings of the declaration
  sentence failed that measurement in opposite regimes: one held document
  chunks to exactly the declared types, the other, allowing the model its own
  types only where nothing declared describes an entity, left nothing to
  propose beyond a form declaration. A measurement, not a promise.
  Enforced after the model answers, not only asked of it: every proposal
  carries whether its type was declared (`ProposalBasis`), and a relation
  proposed under a declared name whose ends contradict the declaration is
  left out and reported (`RejectionReason.ContradictsDeclaration`) rather than
  returned. One caveat from measurement: handed a
  *partial* declaration under an earlier prompt that asked the model to
  "infer only what nothing declares", it proposed only what was declared and
  inferred nothing beyond it, so a partial declaration reached fewer
  competency questions than no declaration at all (the declared-completeness
  ablation in the test project). The prompt now says a declaration is a
  floor, not a ceiling; whether the model treats it that way is what the
  ablation's "inferred reach" column measures — a measurement, not a promise.
- **Confidence routes, it doesn't decide.** Every proposal carries a
  confidence score; a caller-defined threshold routes it to auto-apply,
  human review, or draft-only. Eyu proposes — it never applies anything.
  The routing is code, not a convention left to the caller: a
  [`RoutingPolicy`](src/Eyu.Core/Routing/RoutingPolicy.cs) holds the
  caller's thresholds (per origin — see §C), `Route` returns the tier, and
  `Trace` wraps the proposal in HoneAI's `ITracedPrediction<T>` provenance
  stamp so any `IHitlGate`-style review flow can consume it. There is no
  default threshold, on purpose: self-reported confidence is not
  trustworthy uncalibrated — see [design rationale, §D](docs/philosophy.md).
- **No claim without a reason.** A response is a structure of
  `{claim, sources[], path[]}`. A claim that can't cite its sources cannot be
  expressed — this is enforced by the output shape, not by a prompt — and a
  source must be a record the call was actually given: a response that cites
  an id it was never shown is refused whole, not passed through — the check
  can see that an id exists, never that the record says what the claim says,
  so one invented citation leaves no ground for trusting the others. Every
  other defect costs only its own element: an entity or relation with a
  missing field or an out-of-range confidence, every entity under a duplicated
  id, and a relation whose end is not a surviving entity of the same response
  are left out, and each appears in `OntologyProposal.Rejections` with a
  `RejectionReason`. Nothing is dropped silently — a caller that wants
  all-or-nothing checks `Rejections.Count`, and a caller measuring the model
  counts the reasons. ("Grounding" throughout means this citation
  back to records — not the normalization of a mention to an ontology term
  id that biomedical extraction tools call ontology grounding; Eyu does no
  such lookup. See [design rationale, §B](docs/philosophy.md).)

## What Eyu is

- **A judgment library.** Given structure and records, it proposes what the
  entities, relations, and their confidence are — including which records
  refer to the same real-world entity (entity resolution is part of
  proposing what the entities *are*, not a separate concern). An entity
  cites every record it appears in, and says which of those *are* it
  (`EntityProposal.DenotedBy`) — the rest only mention it, the way a work
  order names its machine. Only the denoting records are a claim that they
  are one entity. That's the whole surface.
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
| `IOntologyProposer`  | The core judgment: entities, relations, confidence, and entity resolution (merging records that denote the same entity) — all from the above. Each entity carries its `Name` as the records write it, and each proposal a `VocabularyOrigin` (`Innate` \| `Acquired`) — see [design rationale, §C](docs/philosophy.md) |
| `IGroundingContract` | `{claim, sources[], path[]}` — the shape every answer is expressed in  |
| `IModelClient`       | Provider-neutral inference access (local or hosted)                    |

Each consumer implements `IStructureSource`/`IRecordSample` for its own world
— an owned raw store, a declared schema, a federated read — and gets the same
proposal logic back through `IOntologyProposer`.

Routing is also a value, not a port. A caller builds a
[`RoutingPolicy`](src/Eyu.Core/Routing/RoutingPolicy.cs) from its own
`RoutingThresholds` (auto-apply / review lower bounds, one pair per
`VocabularyOrigin`), then calls `proposal.Route(policy)` for the tier or
`proposal.Trace(policy)` for the proposal wrapped in a HoneAI
`PredictionProvenance` (`SourceLayer = Frontier`, the confidence, the claim as
rationale, `RequiresReview` for every tier a machine may not act on, and the
route / origin / basis / cited record ids as annotations). Those annotation keys
are constants on
[`ProvenanceAnnotations`](src/Eyu.Core/Routing/ProvenanceAnnotations.cs), and the
stamp's value type is `string`: route, origin and basis are enum names, while the
cited ids arrive as a **JSON array of strings** — record ids are caller-supplied
and may contain any delimiter, so they are serialized rather than joined. A
consumer reading the trace parses that one value; the rest are read as-is. Eyu
references only
[`HoneAI.Abstractions`](https://www.nuget.org/packages/HoneAI.Abstractions) —
the zero-dependency contract package — and never implements `IHitlGate`: opening
a gate, awaiting the reviewer, and applying an approved proposal are the
consumer's, because Eyu never applies anything.

The bundled [`HttpModelClient`](src/Eyu.Core/Inference/Http/HttpModelClient.cs) speaks
the OpenAI-compatible `chat/completions` shape; the caller owns the `HttpClient`'s base
address, auth header and timeout. A provider-specific request field the library does not
model — a self-hosted server's thinking control (`chat_template_kwargs`, `reasoning`), a
`temperature` — is passed through an optional `extraBody` of opaque JSON merged into every
request (the OpenAI SDK's `extra_body` convention; `model` and `messages` stay owned by the
client). When the provider reports token counts, `ModelResponse.Usage` carries them
(prompt / completion / total, each optional) verbatim, so a caller can budget context or
bill against the server's own numbers.

The proposer describes the JSON it expects in the prompt, but a sentence in a prompt does not
stop every model from wrapping its answer in a markdown code fence — some self-hosted
instruction models do so routinely — and a fenced answer is refused as invalid JSON (the
exception carries the text, so the cause is visible). The parser does not strip fences: a
formatting violation it absorbed would stop showing up in measurement. So the proposer also
hands the model client the response's JSON Schema (`ModelRequest.ResponseSchema`, strict:
every field required, nothing extra), and `HttpModelClient` sends it as OpenAI structured
output — `response_format: {"type": "json_schema", ...}` — with no configuration. Its
`json_schema` form is used rather than the older `{"type": "json_object"}` because servers do
not all honor the latter: measured against one self-hosted OpenAI-compatible server and
instruction model, `json_object` was accepted and ignored (three of three answers still
fenced), while the proposer's schema sent this way produced parseable JSON on five of five
calls across a one-chunk and a seven-chunk document — and the same calls with structured
output turned off came back fenced again.

A server that rejects `json_schema`, or handles it badly, is the caller's to override: a
`response_format` in `extraBody` replaces the mapped one, and `{"type": "text"}` turns
structured output off.

```csharp
var extraBody = new Dictionary<string, JsonElement>
{
    ["response_format"] = JsonSerializer.SerializeToElement(new { type = "text" }),
};
var client = new HttpModelClient(httpClient, model, extraBody);
```

A caller with its own `IModelClient` maps `ResponseSchema` to its provider's structured
output the same way, or ignores it and relies on the prompt.

Entity resolution is tuned through a value, not a port: `SinglePassOntologyProposer`
accepts an optional [`LinkageOptions`](src/Eyu.Core/Linkage/LinkageOptions.cs) record
covering the record-linkage pre-filter's classification thresholds, its EM iteration
limits, and whether field comparison is exact or similarity-based. Every default
reproduces the behavior of passing nothing, so a caller reaches for it only once a live
run shows the defaults classifying that caller's data badly — what each value does, and
what is still unmeasured about them, is in Status above.

One default is a premise rather than a tuning value. The pre-filter assumes **a record
is one mention of one entity** — a row, a form submission, a directory entry — so that
two records agreeing on their fields is evidence they denote the same thing. A document
fragment is not that: a text chunk with a title and a path names many entities and
denotes none, and there the premise inverts — measured, chunks of one document agree on
their metadata, the estimator reads the agreement as identity, and the whole document is
pre-linked as one entity before the model sees it. Records of that kind still propose and
ground correctly (a chunk id is a fine source id); pass
`LinkageOptions` with `RecordsDenoteEntities: false` and no pair is compared, every
record stays its own singleton, and the prompt carries no pre-linked groups or gray-zone
pairs. With the pre-filter off there is no linkage prior, so nothing adjusts the model's
self-reported confidence up or down — it is carried through as given. The cited sources then
no longer identify an entity either (a chunk cites many), and no chunk denotes one —
`EntityProposal.DenotedBy` is always empty here, whatever the model answered — so
`EntityProposal.Name` — the entity as the records write it — is what a caller links and stores by; `EntityId` only ties
relations to entities inside one proposal and must never be persisted as an identity. Keep one document
per batch, and mind that every record is rendered into the
prompt in full — the batch size is bounded by the model's context, not by Eyu.

## Exporting as RDF/OWL

`Eyu.Rdf` writes an `OntologyProposal` as an OWL ontology in RDF Turtle, so a proposal can be
opened in an OWL editor, loaded into a triple store or checked by a SHACL validator:

```csharp
var turtle = OntologyTurtle.ToTurtle(proposal, new RdfExportOptions(new Uri("https://example.org/plant#")));
```

Each distinct entity type becomes an `owl:Class`, each distinct relation name an
`owl:ObjectProperty`, each entity an `owl:NamedIndividual` and each relation an assertion between
two of them. The claim, the cited records, which of them denote an individual (`eyu:denotedBy`), the
confidence and whether a type was declared travel as annotations — on a relation, as an OWL axiom annotation (`owl:Axiom`), the form an OWL editor attaches
to the assertion itself. Acquired terms are minted
under the namespace you pass; innate ones are Eyu's own terms, and neither is aligned to an outside
vocabulary. Rejections are not written. The package depends on nothing beyond `Eyu.Core`.

An individual's IRI never comes from `EntityId`, which the model picks afresh on every call. It is
derived from the entity's name and type, compared ignoring case and separators, together with the
records that denote it when any does — the records alone are not enough, since a model reading one
row that reports an event says the row denotes the aircraft, the part and the event alike. The key is
hashed under `entity/` in your namespace, so two exports of the same records name one entity alike
and a triple store merging them merges its individuals. The IRI holds only as long as its inputs do:
a renamed entity, a type spelled differently (a declared vocabulary holds types still) or a different
set of denoting records is a different IRI, and two different things with one name, type and set of
denoting records share one — within one proposal too, where a model reading a document chunk by chunk
proposes the same company once per chunk: those entities are one individual carrying every claim.
`OntologyTurtle.IndividualIris` returns the IRI each entity is written under, for linking your own
triples to them.

Not published yet: it ships with the next release, alongside the two packages above.

## Further reading

[Design rationale](docs/philosophy.md) — why judgment requires structure
first, what kind of thing a proposed ontology is, and why proposals carry an
innate/acquired origin tag, each with a confidence grade on how
well-anchored the reasoning is.

## License

[MIT](LICENSE)
