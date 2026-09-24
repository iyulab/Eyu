# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/) within the 0.x range, where a minor
bump may carry a breaking change.

The release workflow refuses to publish a version this file does not record, so
every published version has a section here.

## Unreleased

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

- `Formbase.Core` to 0.10.1. The release is a patch carrying its own dependency round; the surface
  the coupling smoke test consumes is unchanged.
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
