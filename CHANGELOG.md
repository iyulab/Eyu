# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/) within the 0.x range, where a minor
bump may carry a breaking change.

The release workflow refuses to publish a version this file does not record, so
every published version has a section here.

## Unreleased

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
