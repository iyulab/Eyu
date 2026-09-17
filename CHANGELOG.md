# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/) within the 0.x range, where a minor
bump may carry a breaking change.

The release workflow refuses to publish a version this file does not record, so
every published version has a section here.

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
