# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/) within the 0.x range, where a minor
bump may carry a breaking change.

The release workflow refuses to publish a version this file does not record, so
every published version has a section here.

## Unreleased

### Dependencies

- **`Eyu.Formbase` builds on `Formbase.Core` 0.29.0** (was 0.28.0), for the form type's business name.

### Added

- **A declared structure can carry the name a person reads it by** (`DeclaredStructure.BusinessName`),
  as a declared field can — «설비 점검일지» for `inspection_log`. The prompt shows it beside the declared
  type in quotes, as it shows a field's, and an entity proposed under the business name is written under
  the declared type's name, so the declared type stays the one name in use. The base prompt fingerprints
  do not move; only a call whose declarations carry a business name sends a different prompt.
- **`Eyu.Formbase` carries a form type's business name** (`FormTypeHints.BusinessName`, Formbase 0.29.0)
  into the declared structure, as it already carried each field's.

### Fixed

- **A connection that closes while a streamed answer is still coming is an `HttpRequestException`**,
  as the same cut is for a non-streamed call, rather than an `IOException` escaping the client. The
  exception carries the `HttpRequestError` (`ResponseEnded` for a body cut short), where the request
  went, how much of the answer had arrived, and the transport failure as its inner exception — a
  caller that retries or counts failed calls now does so for this failure too.

## 0.13.0

A minor. A proposer takes type names the caller already uses (`KnownType`) and proposes an entity of a
kind one of them names under that name, spelled as it is in use; a known entity's type counts as a name
in use too. What earlier calls found — known entities, mentioned entities, known types — is passed as one
`PriorKnowledge`: the overloads that took the first two are gone (breaking; migration below). A matched
known entity keeps its type, a declared field can carry the name a person reads it by, `HttpModelClient`
can stream, and an error inside a streamed answer is reported as a failed request. Pairs with Formbase
0.28.0. The known-entities prompt fingerprint moves (`2bc58427` → `d911c6a8`); calls with known types
stamp `KnownTypesPromptFingerprint`.

### Changed

- **An error the endpoint reports inside a streamed answer is an `HttpRequestException`**, as a
  non-success status is, rather than an `InvalidOperationException` — a caller that retries or counts
  failed calls on transport errors now does so for a gateway that fails mid-stream too.

- **Breaking: what earlier calls found is passed as one `PriorKnowledge`.** `IOntologyProposer` has
  two overloads, `ProposeAsync(declared, records)` and `ProposeAsync(declared, records, prior)`; the
  overloads taking `knownEntities` and `knownEntities, mentionedEntities` are gone. Migrate
  `ProposeAsync(d, r, known)` to `ProposeAsync(d, r, PriorKnowledge.Of(known))` and
  `ProposeAsync(d, r, known, mentioned)` to `ProposeAsync(d, r, new PriorKnowledge(known, mentioned, []))`.
  An implementer writes the `PriorKnowledge` overload. Requests and prompt fingerprints are unchanged
  for the same inputs.
- **An entity matched to a known entity keeps the known entity's type.** `EntityProposal.KnownEntityKey`
  already made the known entity win on identity; its type now wins too, so a thing an earlier call
  exported as `Equipment` is not stated as a `Machine` by the next one. The prompt also asks for a
  known entity's type on any entity of the same kind, matched or not, instead of a new name for it.
  The known-entities prompt fingerprint moves (`2bc58427` → `d911c6a8`): measurements taken with known
  entities before this are not comparable with ones after it.

### Added

- **Known types: type names a caller already uses.** `PriorKnowledge.KnownTypes` takes `KnownType`
  (a name, and records of its kind as specimens — compared, never cited). The prompt lists them and
  asks for that name on an entity of a kind one of them names, rather than a new name for the same
  kind; the parse writes an answered type that spells a name in use — a known type's or a known
  entity's — as that name (`TypeNames.SameType`: case and non-alphanumerics set aside, so `WorkOrder`
  becomes `work_order`, while `item` stays `item`). A call with known types stamps
  `KnownTypesPromptFingerprint`; one without them sends the request it sent before.
- **`HttpModelClient` can stream the answer.** `stream: true` sends `"stream": true` (with
  `stream_options.include_usage`) and joins the server-sent chunks' `delta.content` until `[DONE]`,
  so a thinking model behind a gateway that closes slow-to-start responses (commonly after 100
  seconds) still answers. An error chunk ends the call with its message; a stream with no content is
  refused, quoting how it began. Off by default; the client then owns `stream` and `stream_options`.
- **A declared field can carry the name a person reads it by.** `DeclaredField.BusinessName`
  («로트 번호» for `lot_no`, after ODCS's `businessName`) is shown to the model beside the machine
  name — `lot_no "로트 번호" (text, required)` — so an abbreviation is not left for the model to guess.
  It is the caller's own name for the field; Eyu still infers no synonyms. `Eyu.Formbase`'s
  `FormbaseStructureSource` carries a Formbase field's `BusinessName` into it (Formbase `0.28.0`).

### Dependencies

- `Formbase.Core` 0.28.0 (was 0.27.0 in 0.12.0).

## 0.12.0

A minor. `Eyu.Formbase` reads a Formbase repeated section — a form type whose rows are cut from its
parent's documents — as records of its own, and declares the section's key field as a reference to the
parent so rows of one sheet are not linked by the sheet's number. A proposer can take linkage parameters
fitted once on the population and score every call with them. `Eyu.Rdf` can write the previous rule's
term IRIs as deprecated and replaced by the current ones. Two constructors change — source-compatible,
not binary-compatible, so code compiled against 0.11.0 recompiles. Pairs with Formbase 0.27.0. No
prompt fingerprint changes.

### Added

- **`Eyu.Formbase` samples a repeated section's rows.** A Formbase form type that is a repeated section
  of another has no documents of its own; `FormbaseRecordSample` now reads it the way Formbase projects
  it — the parent's standing documents, cut into one record per row, each carrying the parent's key
  value — under the parent document's id, a slash and the row's position. Before, sampling a section
  returned nothing.
- **A repeated section's structure declares its key field as a reference to its parent.**
  `FormbaseStructureSource` adds the link the parent declares as its child relation, so the linkage
  pre-filter does not count two rows of one sheet agreeing on the sheet's number as identity evidence
  when the section is read without its parent.

- **A proposer takes linkage parameters fitted once on the population.** `SinglePassOntologyProposer`'s
  new `linkageParameters` scores every call's pre-filter with them and runs no estimate, as
  `LinkagePipeline.Analyze(records, knownEntities, parameters)` already did for linkage alone. A call of a
  few records against known entities had too few pairs to fit and fell back to default parameters — a
  stranger sharing only a common value (a school) with two known people was put to the model as possibly
  either; on the population's scale they are no candidate.

- **`Eyu.Rdf` can retire the previous rule's term IRIs.** With `RdfExportOptions.BridgeRetiredIris`, each
  acquired class and property is also written under the IRI the rule before 0.5.0 minted for its spelling,
  `owl:deprecated true` and `dcterms:isReplacedBy` the current term — the retirement a store that merged
  older exports into its default graph has no other way to read. Off by default; the default output is
  unchanged. An old IRI that a current term holds is never deprecated.

### Breaking

- `SinglePassOntologyProposer`'s constructor gains a third optional parameter (`linkageParameters`) —
  source-compatible, not binary-compatible.
- `FormbaseRecordSample` takes the `IFieldHintSource` as a second constructor argument, to tell a section
  from a form type with documents of its own.

### Dependencies

- `Formbase.Core` 0.27.0 (was 0.25.0 in 0.11.0).

## 0.11.0

A minor. The linkage pre-filter no longer counts fields a caller declared as references as evidence
that two records are one entity, and the check that withdraws a record's claim on an entity it only
names reads the record's own subject. Both need to know which subject a record is of, so `RawRecord`
gains an optional `Subject`, which `Eyu.Formbase` sets; a record without one is read as before.
`RawRecord`'s constructor gains that parameter — source-compatible, not binary-compatible, so code
compiled against 0.10.0 recompiles. Pairs with Formbase 0.25.0. No prompt fingerprint changes.

### Fixed

- **Fields declared as references are no longer counted as evidence that two records are the same
  entity.** Inspection logs of one machine agree on the machine's tag, name and location — fields
  that name the machine, not the log — and the linkage pre-filter read that agreement as identity:
  distinct logs, and the machine's register entry with them, were pre-linked as one entity and the
  model was told not to re-decide. The proposer now compares each record without the fields its
  subject declares as references (a `Reference` relation's `ViaField` on the subject, a `Child`
  relation's `ViaField` on its target). A register entry keeps its own key; two form types that both
  record one thing's own facts can still be linked. A relation that does not say its side is not
  read. Prompts for records without a subject, and the prompt fingerprint, are unchanged.
- **A record's own field named like another subject's reference no longer withdraws its claim.** The
  check that withdraws a record's denotation of an entity it only names in a reference field read
  every relation's field by name, so an equipment register's own `machine` column counted as the
  work order's reference of the same name, and the register entry lost its claim on its machine
  whenever it denoted anything else too. For a record whose subject is known, only that subject's
  declared references are read; a record without one is read as before.

### Added

- **`RawRecord.Subject`** — the declared subject a record is of, optional. `FormbaseRecordSample`
  sets it to the form type it sampled. Without it a record is read as before: a field name alone
  cannot say whether it is a record's own key or its reference to another.
- **`LinkageEvidence`** — `IdentityView(records, declaredStructures)` and
  `ReferenceFieldsBySubject(declaredStructures)`, for callers that run `LinkagePipeline` themselves
  over declared records.

### Dependencies

- `Eyu.Formbase` builds on `Formbase.Core` 0.25.0 (was 0.24.0).

## 0.10.0

A minor. Linkage can match new records against known entities with parameters fitted once on the
population instead of on each call's own pairs, and can block which pairs it compares; similarity
comparison reads Hangul by its letters. Hangul and full-width digits now compare equal in hosts with
`InvariantGlobalization`, as Native AOT hosts usually are. `EstimationStatus` gains a value
(`Supplied`). Pairs with Formbase 0.24.0. No prompt fingerprint changes; existing calls behave as
before, except similarity comparison of Hangul values.

### Added

- **Linkage can be fitted once and then score new records without refitting.** Matching one new
  record against many known entities with `LinkagePipeline.Analyze(records, knownEntities)` fits EM
  on that call's own pairs — about one true match among `N` — and reads whatever most pairs share
  (a school, a year of birth) as identity: measured on 3,000 synthetic person records, every newcomer
  sharing a name with someone was matched to somebody, at roughly 270 ms a record. Now:
  - `LinkagePipeline.EstimateNonMatchAgreement(population)` measures each field's `u` over random
    pairs of a population (every pair up to a budget, a deterministic sample past it). Random pairs
    are nearly all non-matches, so this needs no EM.
  - `FellegiSunterEstimator.Estimate(..., fixedNonMatchAgreement)` fits only `m` and the prior with
    `u` held, for pairs where duplicates gather.
  - `FieldLinkageParameters.FromNonMatchAgreement(u, matchAgreement, matchPrior)` builds parameters
    from measured `u` and a caller-chosen `m`, marked `EstimationStatus.Supplied` (new). `m` has no
    default: it is how often a true duplicate still agrees, and between 0.90 and 0.95 it decided
    whether two people with one name were told apart.
  - `LinkagePipeline.Analyze(records, knownEntities, parameters, options)` scores with the supplied
    parameters; no EM runs. On the same 3,000 records every re-entered person was matched, newcomers
    sharing a name were wrongly matched a third as often, and a record took about 7 ms.

  The existing overloads are unchanged.
- **Blocking rules.** `LinkageOptions.BlockingRules` lists sets of field names; two records are
  compared only when every field of at least one set is present on both and equal (compared as
  exact field comparison compares). Rules are alternatives, so a pair one misses — a misspelt name —
  another can catch (the same birth date and school). They apply to a batch's own pairs and to
  matching against known entities; `EstimateNonMatchAgreement` ignores them, since `u` has to come
  from random pairs. On the 3,000 records above, a name rule or a birth-and-school rule kept every
  match and cut a record from about 7 ms to 2. Without rules every pair is compared, as before.

### Changed

- `Eyu.Formbase` builds on `Formbase.Core` 0.24.0.
- **Similarity comparison reads Hangul by its letters.** With `UseStringSimilarityComparator`, Hangul
  syllables are spelled out as their jamo before Jaro-Winkler compares them. By syllable, `김민준` and
  `김민중` (one final consonant apart) and `김민준` and `김민수` (another name) were both one character
  of three apart, 0.822 each — below the default 0.90 threshold, so a three-syllable name only ever
  agreed when identical, and no threshold could tell a typo from another person. By letter they are
  one and two of nine apart. The threshold now matters for Hangul; it is the data's to set — on
  synthetic Korean person records, 0.90 let distinct names agree and 0.95 still did for some vowel
  changes, and with fitted parameters exact comparison did as well as either.

### Fixed

- **Decomposed Hangul and full-width digits compare equal in hosts with `InvariantGlobalization`.**
  Field values, names and grounding text are compared in Unicode Form KC, so Hangul typed as jamo
  (as text saved on macOS commonly arrives) meets the precomposed spelling and `０１０` meets `010`. In
  a host built with `InvariantGlobalization` — usual for Native AOT — the runtime leaves non-ASCII
  text unnormalized, and the same name in two forms disagreed. Eyu now composes Hangul and narrows
  full-width ASCII itself; the rest of Form KC still depends on the host.

## 0.9.0

A minor. Eyu runs under Native AOT and trimming, and `Eyu.Formbase` carries a Formbase field bound to
another form type as the reference relation it declares — a Formbase consumer that declares references
that way now has them in the prompt as declared relations, and gets the declared-reference
check that 0.8.0 applied only to declared relations. Pairs with Formbase 0.17.2. No prompt fingerprint
changes; no implementer or caller has to change.

### Fixed

- **`Eyu.Formbase` carries a bound field as the reference it declares.** A Formbase field bound to another
  form type (`FieldHint.Target`) now reaches Eyu as a reference relation to that form type, through the
  bound field itself and, when the binding names one, through the field carrying the target record's key
  (`EntityRef.ViaField`) — each named after the field that carries it. Before, the adapter carried
  declared relations only, so a reference declared as a bound field was dropped: the proposer never saw
  it, and a record naming an entity in that field was not withdrawn from denoting it. A field a declared
  relation to the same form type already runs through adds nothing.

- **Eyu runs under Native AOT and trimming.** Every package now declares `IsAotCompatible`, and the
  serialization Eyu does itself — the HTTP model client's request and response, the proposer's parse of
  the model's answer, and routing's sources annotation — uses compile-time generated metadata instead
  of reflection. Before, a Native AOT host failed in `SinglePassOntologyProposer`'s type initializer
  (`NotSupportedException`, before any model call) and the publish reported trim and AOT warnings from
  `Eyu.Core`. CI now publishes and runs a Native AOT executable over every serializing path.

### Changed

- `Eyu.Formbase` builds on `Formbase.Core` 0.17.2, the first release of it that is itself AOT-compatible.

## 0.8.0

A minor. Where a caller declares the field a relation runs through, `Eyu.Core` no longer lets a record
that only names an entity in that field count as denoting it, and reports each such withdrawal. Calls
without such a declaration behave as before, and no prompt changes: `PromptFingerprint`,
`KnownEntitiesPromptFingerprint` and `MentionedEntitiesPromptFingerprint` are unchanged. `Eyu.Formbase`
pairs with Formbase 0.17.1. Additive only — no implementer or caller has to change.

### Added

- **A declared reference field is a mention, never a denotation.** When the caller declares the field a
  relation runs through (`DeclaredRelation.ViaField`), a record holding an entity's name in that field
  (case and whitespace aside) no longer counts as denoting the entity, whatever the model answered — a
  work order naming its machine was read as a record of the machine often enough that a prompt alone
  did not stop it. The record moves from the entity's `DenotedBy` to its `MentionedIn` before linkage
  confidence, known-entity matching and merge candidates read it, and each move is reported in the new
  `OntologyProposal.DemotedDenotations`. A record is never left denoting nothing: when every entity it
  would denote is one it names in a reference field, the claims stand. Calls without such a declaration
  behave as before.

### Fixed

- **Packages carry the license text.** Every package now ships `LICENSE` at its root beside the `MIT`
  license expression, so redistributing a package carries the notice the license requires and tooling
  that collects third-party notices finds the text.

## 0.7.0

A minor. `Eyu.Core` takes the entities earlier calls only mentioned and reports, as merge candidates,
the entities of a call that may be the same things — so identity across calls no longer depends on
which source arrives first. `Eyu.Formbase` pairs with Formbase 0.17.0. Implementers of
`IOntologyProposer` implement a new overload; `PromptFingerprint` and `KnownEntitiesPromptFingerprint`
are unchanged, so measurements of calls without mentioned entities stay comparable across this release.

### Added

- `MentionedEntity` and `OntologyProposal.MergeCandidates` (`MergeCandidate`): a caller can hand back
  entities an earlier call only mentioned — named by records that refer to them, denoted by none — and
  a call that meets a record of a thing that may be one of them reports a merge candidate: the
  proposed entity, the mentioned key, a claim citing this call's records, and the model's confidence.
  A candidate is never an identity (`KnownEntityKey` stays for known entities), so the caller decides
  whether to join the two and the same records yield the same identities whichever source is proposed
  first. A candidate comes only from an entity a record of the call denotes; where records do not each
  denote one entity, none survives. Mentioned entities are not run through the record-linkage
  pre-filter, so a candidate's confidence is unadjusted.
- `SinglePassOntologyProposer.MentionedEntitiesPromptFingerprint`, the fingerprint of the fixed text a
  call with mentioned entities adds. `PromptFingerprint` and `KnownEntitiesPromptFingerprint` are
  unchanged, and a call without mentioned entities sends exactly the request it sent before.
- `ProposalElement.MergeCandidate` and the rejection reasons `UnknownMentionedEntity`,
  `CandidateEntityUnresolved` and `CandidateEntityNotDenoted`: a defective candidate is left out and
  reported like any other element.

### Changed

- `IOntologyProposer`: the overload an implementer writes now also takes `mentionedEntities`; the
  known-entities overload forwards to it with an empty list. Callers are unaffected.
- `Eyu.Formbase` is built against `Formbase.Core` 0.17.0 (was 0.14.0).

## 0.6.0

A minor. `Eyu.Core` matches a call's entities to entities earlier calls identified (known entities),
returns what its record-linkage pre-filter contributed with every proposal, caps the gray-zone pairs a
prompt carries, and keeps a declared relation declared when a model names it by its whole prompt line.
`Eyu.Formbase` pairs with Formbase 0.14.0 and samples records rather than appends. Implementers of
`IOntologyProposer` implement a new overload, and `PromptFingerprint` changes, so measurements taken
before and after this release are not comparable.

### Added

- `OntologyProposal.Linkage` (`LinkageReport`): what the record-linkage pre-filter contributed to a
  proposal — its full result for the call (`Analysis`, which was computed but not returned) and the
  groups it confirmed as one entity that the answer spread over two or more entities anyway
  (`SplitClusters`). A pre-filter that joins different documents on values they copy from a record
  they refer to, and a model that did not follow a confirmed group, used to leave no trace in the
  result. Null for a proposal built directly.
- `LinkageOptions.MaxGrayZonePairsInPrompt` (default 200) caps the gray-zone pairs put to the model.
  Pairs grow with the square of the batch: 104 records produced 2,187 pairs and a prompt larger than
  a 131,072-token context. Past the cap the pairs with the strongest prior toward the same entity are
  kept and the rest are counted in `LinkageReport.GrayZonePairsOmitted`; a batch under the cap sends
  exactly the prompt it did before. A call that put more than 200 pairs to the model now puts 200.

- Known entities: `IOntologyProposer.ProposeAsync` gains an overload that takes the entities earlier
  calls identified (`KnownEntity` — the caller's key, name, type and the records that denoted it). An
  entity the new records show that is one of them comes back with `EntityProposal.KnownEntityKey` set
  to its key — the known entity wins, as a declaration does, but only for identity: the name and type
  stay as the new records write them. A key the call did not supply leaves that entity out
  (`RejectionReason.UnknownKnownEntity`), and a claim still cites only the call's own records — a known
  entity's records are for comparison. Eyu keeps nothing between calls; whoever keeps the proposals
  decides which entities are known. Known entities that cannot mean one thing (a key given twice, or a
  record that is also among the call's records) are refused before the model is asked; one record may
  denote several known entities, as one row can denote an event and the machine it happened to.
  A call without known entities sends exactly the request it sent before (`PromptFingerprint`
  unchanged); one with them adds a clause and a `knownEntityKey` field, fingerprinted separately
  (`KnownEntitiesPromptFingerprint`). Implementers of `IOntologyProposer` implement the new overload;
  the existing one forwards to it.
- Record linkage compares a call's records with known entities' records too (`LinkagePipeline.Analyze`
  overload; `LinkageAnalysis.KnownCandidates`). A known entity's records are compared with the call's
  and never with each other, in the same parameter estimate as the call's own pairs, and never join the
  call's clusters. A record the pre-filter matches to a known entity is told to the model as pre-linked
  to that key, a gray-zone one as a candidate with its prior log-odds, and the confidence of an entity
  matched to a known one reads that evidence (`LinkageConfidenceAdjuster.AdjustForKnownEntity`). Only
  fields of the same name are compared, as within a call: sources that name the same thing under
  different fields are left to the model.
- `Eyu.Rdf` writes an entity matched to a known entity under that entity: a known key that is an
  absolute `http`, `https` or `urn` IRI is the individual's IRI — a caller that passes the IRI an earlier
  export minted keeps it across calls, whatever the new records name the entity — and any other key is
  hashed under `entity/known/`, a branch no name-and-type IRI shares. Entities without a known key are
  minted exactly as before, so `eyu:iriRule` stays `"0.5.0"`: no earlier release produced a known key.

- `Eyu.Rdf` names the rule an export's IRIs were minted under: the ontology carries
  `eyu:iriRule "0.5.0"` (`EyuVocabulary.IriRule`), the release that introduced the current rule, and
  the value changes only in a release that changes how a class, property or individual IRI is
  minted. Individual IRIs survive such a change and term IRIs may not, so a triple store holding
  exports from two rules typed one individual into both the old class and the new one — 0.5.0's
  advice to re-export and replace gave no way to find which triples the older rule wrote. Kept one
  export per named graph, the annotation says which graphs to drop; an export without it was written
  by 0.5.0 or earlier.

### Fixed

- `FormbaseRecordSample` samples records, not appends. Formbase 0.14.0 lets a document correct or
  retire a record; read append by append, every version of a corrected record would be a separate
  record and a retirement, which has no body, would fail. The sampler folds the stream the way
  Formbase's projection does (`RecordFold.Latest`): a corrected record appears once, as its latest
  document, and a retired one not at all. Every document of the form type is read before the first
  `maxCount` records are taken.
- A relation a model named by copying the prompt's whole declared-relation line — name and ends,
  `asset: work_order -> asset` rather than `asset` — came back `Inferred` under a name nothing declares,
  skipped the check that it joins the declared ends, and got a different property IRI in `Eyu.Rdf` from
  run to run. The merge now recognizes that line (it is Eyu's own rendering) and restores the declared
  name, so the relation is `Declared` and checked like any other. The prompt also quotes a declared
  relation's name and says to use the quoted name alone (`Declared relation "asset": work_order -> asset`),
  which changes `PromptFingerprint` for every call: measurements taken before and after this release are
  not comparable.

### Documentation

- `OntologyTurtle`'s summary said a class is written under the first spelling seen; since 0.5.0 it
  is minted from the name as Eyu compares it (`:Workorder`), and the first spelling is its label.
- The clerical-review guide now says what a caller scoring model-made clusters must supply: each
  sampled record's candidates include its nearest members of *other* predicted clusters, not only
  singletons, and merged clusters are stratified by recall risk as well as precision risk. Without
  the first, a model that keeps near-duplicates in neighbouring clusters loses no measured recall.

### Dependencies

- `Eyu.Formbase` now takes `Formbase.Core` 0.14.0 (was 0.11.1). 0.13.0 breaks `QuerySpec.Filters`, which
  `Eyu.Formbase` does not read; since 0.12.0 `DocumentBody.From`/`Parse` refuse JSON that names a property
  twice in one object; 0.14.0 gives documents a record identity (see the next entry).

## 0.5.0

A minor with three breaking changes: the IRIs of acquired classes and properties in `Eyu.Rdf`, clerical-review strata named rather than kinded in `Eyu.Core`, and text compared in one Unicode form across both.

### Changed

- **Breaking (`Eyu.Rdf`)**: an acquired class or property is now minted from its name compared the way
  Eyu compares names — case and separators ignored — with a class's first letter upper-cased and a
  property's lower-cased (`Work Order` and `WorkOrder` → `:Workorder`; `maintains` and `Maintains` →
  `:maintains`). It used to be minted from the spelling the proposal used, while the individual's IRI
  already ignored case and separators in the type — so two exports that spelled a type differently
  named one individual and put it in two unrelated classes when a triple store merged them. The
  spelling a proposal used stays as the term's `rdfs:label`. A graph holding 0.4.0 exports keeps the
  old class and property IRIs; re-export to replace them. Innate terms and individual IRIs are
  unchanged (but see the next entry for names written in another Unicode form).
- **Breaking (`Eyu.Core`, `Eyu.Rdf`)**: wherever Eyu decides two pieces of text are the same, it now
  compares them in Unicode Normalization Form KC — type and relation names (declared against
  proposed, innate or acquired, and the IRIs `Eyu.Rdf` mints from them), field values in
  `FieldComparator`, and the words a claim shares with its sources in `GroundingOverlapCheck`. Hangul
  decomposed into jamo (NFD — how text saved on macOS commonly arrives) and precomposed Hangul render
  identically but were different code points, so a declared `작업지시` did not match a proposed one,
  the same name scored a Jaro-Winkler similarity of 0 against itself, a claim quoting its source was
  judged ungrounded, and two exports put one entity in two classes; full-width Latin letters and
  digits likewise did not meet their ASCII forms. `Eyu.Rdf` also writes names, labels and claims in
  NFC, as RDF 1.1 asks of IRIs and literals; record ids and field names are written as given, since a
  consumer joins back on them. Input already in NFC and ASCII compares and exports exactly as before;
  what changes is the class, property and individual IRIs of names written in a decomposed or
  compatibility form, and a name's letters outside the Basic Multilingual Plane, which the comparison
  used to drop. `FieldComparator`'s exact match ignores leading and trailing whitespace, not whitespace
  inside a value — its documentation said otherwise.
- **Breaking (`Eyu.Core`)**: a clerical-review stratum is named, not kinded. `SampledStratum.Kind`
  and `StratumExclusion.Kind` are now `Name` (a string); the pre-filter's three strata are named
  after their `ReviewStratumKind` (`Singleton`, `CleanMerge`, `Contested`), and a seeded pilot over
  them draws exactly the records it drew before.
- A model call answered outside the success range now throws `HttpRequestException` whose message
  names the status, the request URI (without its query) and an excerpt of the body the server
  answered, with the status on `StatusCode`. It used to say only the status, so the usual cause — a
  base address missing the API version segment or its trailing slash, which sends the request to a
  path the server does not serve — could not be told from a wrong model name or a rejected field
  without re-running the call. A `404` also says how the path is resolved.

### Added

- Clerical review over clusters and strata that do not come from the pre-filter:
  `ClericalReviewSampler.DrawPilot(IReadOnlyList<PopulationStratum>, …)` draws over named strata the
  caller formed (they must partition the population), and `ClericalReviewScoring.Score(clusters,
  candidates, …)` scores against predicted clusters and per-record candidates given directly — for
  clusters a model made, or strata from a similarity band when the pre-filter's posteriors are not
  identified. The `LinkageAnalysis` forms are now written on top of them.
- `LinkageErrorRateCaveat.TooFewFields`: an unlabeled error-rate estimate over fewer than three
  compared fields (`FellegiSunterEstimator.MinimumFieldsForIdentification`) is no longer reported as
  reliable. A two-class mixture of independent binary agreements is not identifiable from fewer than
  three; on records of one short text field the fit converged to an arbitrary prior, left every pair
  to the model, and reported both rates as 0 with no caveat. `docs/clerical-review.md` §6 says how to
  stratify a review when the model cannot.
- The packages carry their XML documentation, so an IDE shows each member's comment from the package.

### Documentation

- The README has a quick start: records in, a proposal and its Turtle out, including the base-address
  rule and the timeout a thinking model needs. CI compiles it against freshly packed packages and
  exactly the packages the README installs.

### Dependencies

- `Eyu.Formbase` now takes `Formbase.Core` 0.11.1 (was 0.11.0), a patch with no surface change.

## 0.4.0

A minor that carries breaking changes, as 0.x minors may. An entity now says which of the records it
cites are it (`DenotedBy`), which changes the response schema and the prompt fingerprint; the
record-linkage EM fits its own match prior; and a clerical review's interval no longer collapses on a
stratum that showed no error. A third package, `Eyu.Rdf`, writes a proposal as OWL in Turtle.

### Changed

- The record-linkage EM (`FellegiSunterEstimator.Estimate`) estimates its own match prior. The
  prior used to share the [0.01, 0.99] bounds of the per-field agreement probabilities, and past
  about a hundred records a deduplication batch's true prior is below 1%, so the fitted prior was
  the bound itself (0.01 against 0.001 on a 1,000-record benchmark) and every posterior was pushed
  toward match tenfold. It now carries Jeffreys' pseudo-count instead (`PriorPseudoCount`), which
  keeps it inside (0, 1) and vanishes as pairs accumulate: on a labeled benchmark the fitted prior
  is now the true share of matching pairs (0.00100 on 1,000 records). Because the posteriors move,
  so do the fitted agreement probabilities and a few classifications — B-cubed F1 moved by at most
  0.0015 on that benchmark, in both directions — and the unlabeled error-rate estimate: its
  false-non-match rate went from 0.00498 to 0.00029 where the labels say 0. The agreement probabilities keep their
  bounds, now public and documented as what they are — an evidence cap
  (`AgreementProbabilityFloor` / `AgreementProbabilityCeiling`): letting them fall to their true
  values cut precision from 0.986 to 0.950 on names and addresses alone. Batches under
  `MinimumPairsForEmEstimation` pairs still use the heuristic default.
- A stratum of a clerical review whose reviewed scores are all alike no longer contributes zero
  variance to `ClericalReviewEstimator.Estimate`. It contributes `p (1 − p)`, with `p` the exact
  95% upper bound on the fraction of the stratum that could still differ when none of the `n_h`
  reviewed records did (`UnobservedVariationBound`), so a pilot that happens to see no error
  reports how uncertain that leaves the score instead of a zero-width interval. On a labeled
  benchmark the old interval contained the true recall 4 times in 100. Normal intervals from such
  samples are now wider, often past 1.0; the bootstrap still cannot resample an unvarying stratum,
  and while `NoVariationInStratum` is set the normal interval is the one to read.
- An entity now says which of the records it cites *are* it. `EntityProposal.DenotedBy` lists the
  cited records that denote the entity, and `MentionedIn` the rest — records that only refer to it,
  the way a work order names the machine it ran on; the claim's sources are still every record the
  entity appears in. The response schema and prompt ask the model for `denotedBy` alongside
  `sources`, so the prompt fingerprint changes and measurement runs before and after this release
  are not comparable. The linkage confidence adjustment now reads only `denotedBy`: until now it read
  every citation as a claim that the cited records are one entity, so an entity many rows refer to —
  a machine, a plant, a manufacturer — had its confidence pulled toward zero because those rows are
  different rows (0.9 → 0.1 for a machine two work orders and its master row cite), while an entity
  cited by rows the pre-filter had linked was pushed to 0.999 whatever the model said. A claim that
  two non-matching records are one entity is penalized exactly as before. An answer that omits
  `denotedBy` claims no identity and nothing is adjusted; a denoting record missing from `sources`
  is added to them; a `denotedBy` id the call was never given refuses the response like any invented
  source. `EntityProposal.Create` takes an optional `denotedBy`, defaulting to every cited record.
  Where records do not denote entities (`LinkageOptions.RecordsDenoteEntities: false`, document
  chunks) `DenotedBy` is always empty: a chunk mentions what it describes and is not a record of it,
  and models asked for `denotedBy` there fill it anyway, differently on every call. A record named in
  it is kept as a citation.

### Added

- `Eyu.Rdf`, a third package: writes an `OntologyProposal` as an OWL ontology in RDF 1.1 Turtle
  (`OntologyTurtle.ToTurtle` / `Write`, with `RdfExportOptions` naming the caller's namespace). Entity
  types become classes, relation names object properties, entities named individuals, and relations
  assertions between them; each element's claim, cited records, confidence and basis are carried as
  annotations in an Eyu vocabulary (`EyuVocabulary`), a relation's as an OWL axiom annotation
  (`owl:Axiom`), so an OWL reader keeps them attached to the assertion. An individual also carries
  `eyu:denotedBy` for each cited record that is a record of it. An individual's IRI is derived from
  its name and type together with the records that denote it, when any does — not from `EntityId`,
  which the model picks afresh on every call — so two exports of the same records name one entity
  alike (`OntologyTurtle.IndividualIris` returns them), and entities one proposal gives the same key
  are one individual carrying every claim. Names Eyu compares as one — case and separators ignored — become one term. Innate
  types and relations are written as Eyu's own terms, not aligned to any outside vocabulary.
  Rejections are not written. Until now a proposal could only be read by code that knew Eyu's record
  types. The package takes no dependency beyond `Eyu.Core`: its tests read the output back with an
  independent parser, but the writer itself does not ship one.

### Documentation

- The README states the current release and how to install both packages at it. Until now nothing
  in the documents named a version, so a reader had no way to tell which release the text
  described, and the pairing with Formbase was stated by package name only.

### Dependencies

- `Formbase.Core` to 0.11.0. The release adds a raw-stream read and changes the public
  `NotProjectedException` constructor, neither of which the adapter or the coupling smoke test
  touches; the surface they consume is unchanged.
- `Microsoft.NET.Test.Sdk` to 18.10.1 (tests only).

## 0.3.0

### Changed

Every entry in this section is a breaking change.

- `IOntologyProposer.ProposeAsync` takes `IReadOnlyList<DeclaredStructure>` — one declaration
  per subject — instead of a single nullable `DeclaredStructure`. Pass `[]` where you passed
  `null`, and `[structure]` where you passed one. Records that name several kinds of thing (a
  document chunk names companies, people and products at once) can now have each kind declared,
  and a type can be declared by name alone (`new DeclaredStructure(subject, [], [])`). Declaring
  the same subject twice (compared ignoring case and separators) throws `ArgumentException`
  before the model is called.
- The declaration clause of the prompt asks the model to propose an entity a declared type
  describes under that type, as it already asked for relations, and says the declared types and
  relations are not the only ones, so `SinglePassOntologyProposer.PromptFingerprint` changes.
  Each declared relation line now names the subject it leaves (`asset: work_order -> asset`).

### Added

- Every entity whose type is any declared subject is stamped `ProposalBasis.Declared`. A relation
  under a declared name is kept as declared when its ends match any declaration of that name, and
  is rejected with `RejectionReason.ContradictsDeclaration` when they match none. Declarations
  still do not filter or rename: an undeclared type such as `Company` next to a declared
  `Organization` is returned as inferred, unchanged.

### Internal

- The CI and release workflows run the Node.js 24 majors of the actions they use (`actions/checkout`
  v7, `actions/setup-dotnet` v6, `actions/cache` v6). The Node.js 20 majors ran only because the
  runner forced them onto Node.js 24; no input any step passes changed meaning.

## 0.2.0

### Changed

Every entry in this section is a breaking change.

- `EntityProposal` carries a `Name` — the entity as the records write it — and
  `EntityProposal.Create` takes it as its second argument (required, non-blank).
  `EntityId` still identifies an entity only within one proposal; `Name` is what a caller
  links and stores by, which matters most with `RecordsDenoteEntities: false`, where the
  cited sources no longer identify an entity.
- `OntologyProposal` takes a third member, `Rejections`: every element of the model's
  answer that the proposal does not carry, with a `RejectionReason` and a readable detail.
- `SinglePassOntologyProposer` no longer refuses a whole response for a defect in one
  element. An entity or relation with a missing field or an out-of-range confidence, every
  entity under a duplicated id, a relation whose end names no entity of the response or an
  entity that was itself rejected, and a relation that contradicts declared structure are
  left out and reported in `Rejections`. Invalid JSON and a citation of a record the call
  was not given still refuse the whole response with a `FormatException`. Previously the
  element-level defects surfaced as `FormatException`, `ArgumentException` or
  `ArgumentNullException` depending on the field.
- `VocabularyOrigin` is stamped by the proposer from the new closed `InnateVocabulary`
  (entity types Person, Organization, Event, Action, Location, Time; relation names PartOf,
  ParticipatesIn, LocatedIn, OccursAt; compared ignoring case and separators, with no
  synonyms) instead of being read from the model, which is no longer asked for it.
- The prompt's response schema adds `name` to entities and drops `origin`, so
  `PromptFingerprint` changes; measurement runs made before and after are not comparable.
- `ModelRequest` takes an optional second member, `ResponseSchema`: a JSON Schema the
  response is expected to satisfy. `SinglePassOntologyProposer` sends its response schema
  with every call, and `HttpModelClient` maps it to `response_format` `json_schema`
  structured output (strict) unless `extraBody` carries its own `response_format`. A request
  without a schema sends no `response_format`, as before. `PromptFingerprint` now also covers
  the response schema.

### Added

- `InnateVocabulary`, `ProposalRejection`, `ProposalElement` and `RejectionReason`.
- README: why structured output uses the `json_schema` form (not every server honors
  `json_object`), and overriding or turning it off through `extraBody`.

### Fixed

- `GroundingOverlapCheck` counted only ASCII letters and digits, so a claim written outside
  ASCII had no tokens and was reported unsupported whatever its source said. Words in any
  script now count, and Chinese, Japanese and Korean text is compared as overlapping
  two-character tokens, so a Korean claim matches the record it restates even where the
  particles differ. Results for ASCII-only text are unchanged.

### Dependencies

- `Eyu.Formbase` now takes `Formbase.Core` 0.10.0 (was 0.9.0). The declared-structure
  adapter reads form types and columns only, none of which changed; the port that broke in
  that release (`IProjectionState`) is not one this package implements.

## 0.1.0

First published version. What it contains is what the README describes; the
short form:

### Added

- `Eyu.Core` — the judgment contract: `IStructureSource`, `IRecordSample`,
  `IOntologyProposer`, `IGroundingContract`, `IModelClient`, with a single-pass
  reference proposer (`SinglePassOntologyProposer`) over an OpenAI-compatible
  chat-completions client (`HttpModelClient`).
- `HttpModelClient` takes an optional opaque `extraBody` of request fields merged
  into every request — a self-hosted server's thinking control
  (`chat_template_kwargs`, `reasoning`), a `temperature`, any provider-specific
  field, passed through verbatim (the OpenAI SDK `extra_body` convention);
  `model` and `messages` stay owned by the client. `ModelResponse.Usage`
  (`TokenUsage`: prompt / completion / total, each optional) carries the
  provider's token counts when it reports them. `SinglePassOntologyProposer.PromptFingerprint`
  stamps measurement reports so runs made across a prompt edit are not read as
  comparable by accident.
- Grounded proposals: every entity and relation carries the record ids it was
  drawn from, and a response citing a record the call never supplied is refused
  rather than returned.
- Declared structure: a caller's declaration is authoritative — declared types
  are stamped `ProposalBasis.Declared`, and a relation proposed under a declared
  name whose ends contradict the declaration is dropped. The prompt asks the
  model to treat a declaration as a floor, not a ceiling.
- Entity resolution inside the proposal: a Fellegi-Sunter pre-filter (EM-fitted
  per batch, with a heuristic fallback for small batches) pre-links confirmed
  matches, hands gray-zone pairs to the model, and folds the prior into each
  claim's confidence. Opt-in Jaro-Winkler field comparison for notation
  variants. `LinkageOptions.RecordsDenoteEntities` turns the pre-filter off
  for records that are document fragments rather than entity mentions, where
  its premise does not hold.
- Confidence routing: `RoutingPolicy` with per-origin (`Innate` / `Acquired`)
  thresholds; Eyu proposes and never applies.
- Clerical review support for measuring entity-resolution accuracy without a
  labelled set: stratified sampling, record-level scoring, agreement statistics,
  Rao-Wu bootstrap intervals and a capped Neyman allocation
  (`docs/clerical-review.md`).
- `Eyu.Formbase` — `IStructureSource` and `IRecordSample` adapters over
  Formbase form declarations and rows.
- Quality instruments in the test project: competency-question reach, a
  declared-completeness ablation ladder with Spearman correlation, and a live
  measurement suite that runs only when an endpoint is configured.
