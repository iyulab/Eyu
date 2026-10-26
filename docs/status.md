# Status

What has been measured about Eyu, what each measurement establishes, and what it does not. The
README carries the one-paragraph summary; this page carries the detail.

## In one line

Contract implemented; grounding integrity and response-parsing reliability validated against a real
model; entity-resolution accuracy measured on labeled public benchmarks (B-cubed F1 0.99–1.00 on
Febrl synthetic person records; pair-level F1 0.93–1.00 from the pre-filter alone on clean structured
entity-matching sets, with most pairs left to the model on product, dirty and long-text sets) and not
yet reviewed by people on domain data; ontology extraction measured on two public tasks (Text2KGBench
triples, LLMs4OL type relations — below); exercised by one external package
consumer on a Korean-language document corpus, whose first measurement is what the entity `Name`,
per-element `Rejections` and stamped `VocabularyOrigin` answer.

## What exists

All five ports exist as C# types (`Eyu.Core`), plus a first source adapter (`Eyu.Formbase`) and an
RDF/OWL writer (`Eyu.Rdf`). All three are published on NuGet, and `Eyu.Core` is referenced by the
external consumer named above; inside this repository the
[coupling smoke test](../tests/Eyu.IntegrationSmoke.Tests/SchemaProposerCouplingSmokeTests.cs)
references `Formbase.Core` the same way, as a package.

## Grounding integrity

`IOntologyProposer`'s judgment logic (`SinglePassOntologyProposer` + `HttpModelClient`) is implemented
and has been measured against a real model (GPUStack `qwen3.8-27b`) across two record domains, and over
two Korean company-profile documents passed as chunks — every cited source id existed among the records
given, 0 violations every time.

That check alone does not prove a citation actually backs its claim, so the same measurement also runs a
mechanical content-overlap check ([`GroundingOverlapCheck`](../src/Eyu.Core/Grounding/GroundingOverlapCheck.cs))
between each claim and the record content it cites — text in any script, with Chinese, Japanese and
Korean compared as overlapping character pairs so a Korean claim still matches the record it restates
when the particles differ. It is a heuristic signal to spot-check per run, not a confirmed defect count
(see the type's doc comment for why token overlap is not a semantic verifier); a run's own counts are in
its report, not restated here, since the pre-filter below changes them run to run.

## Type relations

A proposal can say that one type is a kind of another (`TypeRelations`, asked for with
`proposeTypeRelations` or by declaring a super type). The wiring — the request unchanged when not asked
for, the declared pair stamped declared, an inverted one rejected — is held by unit tests; how often a real
model proposes the type relations a text supports is measured on the LLMs4OL 2026 Reuse task (ontology
extension), whose expected additions are 93.5% `is-a` triples — a share that was out of reach before this
channel existed. On the task's full test set — 1,799 samples — with `qwen3.8-27b`, each sample's initial
ontology declared and only triples on its own vocabulary kept, every sample's triples scored together:
`is-a` precision 0.92, recall 0.79 (F1 0.85); `instance-of` precision 0.99, recall 0.55; the task's graph
similarity 0.78 under exact match and 0.79 under semantic match (0.79 and 0.80 as the mean over samples).
Calls the model endpoint failed were run again until none was left, so no sample is scored on an empty
answer.

What is missed, and why. Of the `is-a` triples proposed that the expected answer lacks (about 490), three
in four place a term the expected answer never mentions — a narrower concept the text does contain, such
as `clay mineral` under `mineral` — and they gather in a few samples; the rest attach a term the answer
knows to a different parent. Of the 467 expected `instance-of` triples, 190 type a term as `document` or
`named individual` — labels of how the source ontology was packaged (a file name such as `ham.ttl`, an OWL
individual) rather than kinds a text supports — and Eyu proposes 71 of them; without those two labels,
`instance-of` recall is 0.68. Of the 208 missed, 53 name a term the sample's text does not contain — the
task hands its terms in as input, while Eyu does not raise an entity no record supports — and 82 are terms
in the text left untyped, many of them adjectives of state (`dense`, `triclinic`) or chemical symbols.

For comparison, a published participant result — Qwen2.5-14B-Instruct with two retrieved examples per
sample — reports graph similarity 0.869 under semantic match. That paper states neither the split nor how
samples are aggregated, so the two figures bound a range rather than rank a pair: this run uses no
retrieved examples and no task-specific prompt.

The challenge's final Reuse leaderboard ranks teams by averaged graph similarity under exact match. Against
this run's 0.79 (the mean over samples; 0.78 pooled) the five teams ahead score 0.85 to 0.98 and the teams
below it 0.70 or less. The leaderboard page does not say whether its scores come from the test inputs and
the scoring this run used, so it too bounds a range rather than ranks this run.

## Triple extraction against a declared ontology

Text2KGBench (Wikidata-TekGen) hands an ontology — concepts, and relations from a domain concept to a
range concept — and sentences, and scores the triples extracted from each sentence. Each concept is
declared as a structure with its relations; each sentence is a document-fragment record
(`RecordsDenoteEntities = false`), ten to a call; the benchmark's own scorer scores the result. On its
human-verified sentences, with `qwen3.8-27b`:

| ontology | output | precision | recall | F1 | ontology conformance | relation hallucination | baseline F1 |
|---|---|---|---|---|---|---|---|
| movie (174 sentences) | everything proposed | 0.39 | 0.34 | 0.35 | 0.67 | 0.33 | 0.28 |
| movie | declared basis only | 0.39 | 0.34 | 0.35 | 1.00 | 0.00 | |
| sport (97 sentences) | everything proposed | 0.24 | 0.23 | 0.22 | 0.36 | 0.64 | 0.50 |
| sport | declared basis only | 0.24 | 0.23 | 0.22 | 0.98 | 0.02 | |

Precision and recall are the same for both outputs because the scorer compares only triples on the
relations a sentence's gold triples use; what the declared-basis filter changes is conformance and
hallucination — a consumer that keeps the declared basis gets the ontology's relations alone. The
baseline is the Vicuna-13B result the benchmark ships, prompted with the most similar training sentence
as a worked example; Eyu is given no example.

What the sport numbers are made of, read sentence by sentence:

- A quarter of the gold triples (37 of 141) sit on four declared relations the model never used. It
  stated the same facts under names of its own — "the 28th edition of the Paris Masters" as
  `edition_of` where `sports season of league or competition` is declared. Three of the four have a
  range the ontology gives only as an unlabeled identifier or not at all; for the fourth, `occupation`,
  half the gold objects are occupation labels the sentence does not spell out.
- Most gold `sport` triples have a person as subject, while the ontology declares `sport` from a sports
  competition; Eyu rejects a declared relation whose ends the declaration does not describe, and the
  gold triples break the declaration here.
- Gold names are the knowledge base's labels, not always the sentence's words ("Terrence Jones" for
  "Terrence Alexander Jones", "athletics competitor" for "track and field athlete"); Eyu proposes the
  surface form the record shows.
- The model typed ends of declared relations more specifically than the declaration (an athlete where
  `human` is declared, a film genre where `genre` is); those relations are rejected. Their exact gold
  matches would add at most 7.5 points of recall on sport and 4 on movie. A declaration that names the
  narrower type a kind of the declared one (`SuperTypes`) now admits them; this ontology declares no
  such kinds.

## The record-linkage pre-filter

`SinglePassOntologyProposer` takes an optional `LinkageOptions` (`Eyu.Core.Linkage`) that runs a
Fellegi-Sunter record-linkage pre-filter before the model call: record pairs it confirms as a match are
injected into the prompt as "merge them, do not re-decide", gray-zone pairs are injected as a log-odds
hint for the model to judge itself, and confirmed non-matches get neither — so the prompt carries a
resolution instruction, where before it carried none. The linkage evidence then adjusts an entity's
confidence only through the records the entity claims denote it (`DenotedBy`), never through records
that merely mention it: a machine several work orders name is not penalized because the work orders are
different orders.

A live run measured that this classification is observable (per-pair Match/GrayZone/NonMatch, EM
convergence status, match prior) and that changing the thresholds measurably changes both the resulting
prompt and the grounding-overlap counts. What that run does **not** establish: whether any particular
threshold setting is more *correct* — no labeled ground truth exists in these domains to score the
pre-filter's own match/non-match calls against — or whether the proposer's self-reported confidence is
informative on its own, which it was not in an earlier measurement (see
[design rationale, §D](philosophy.md)); gray-zone cases combine it with the Fellegi-Sunter prior via a
Bayesian update instead of using it alone.

## Entity-resolution accuracy

On labeled public benchmarks the pre-filter has been scored — [linkage-benchmark.md](linkage-benchmark.md):
B-cubed F1 0.99–1.00 on Febrl person records, 0.991–0.994 on names and addresses alone with exact
comparison. On the thirteen entity-matching benchmarks of the DeepMatcher study (two tables, labeled
candidate pairs, pair-level F1 on the test split, unsupervised): 0.93–1.00 from the pre-filter alone on
clean structured sets; on product, dirty and long-text sets 87–100% of pairs fall in the gray zone the
model adjudicates, and the ratio separates matches weakly where whole field values rarely agree (best
single threshold 0.19–0.34 on Amazon-Google and Abt-Buy, and 0.40 on Company — no better than calling
every pair a match) — a comparator ceiling, not a threshold one.

Scoring one new record against a known population (fit once on the population, score each record) has
been measured on the same Febrl records ([linkage-benchmark.md](linkage-benchmark.md#one-new-record-against-a-known-population)):
easy with an identifying field; with names and a town only, gray candidates grow with the population, and with
surnames as concentrated as in Korea 30% of strangers classify as `Match` at 1,500 known people — two in three
of them sharing the full name. Value frequencies (`FieldLinkageParameters.ValueFrequencies`) reduce that only
slightly. Not measured: a real register's name distribution, and what a reviewer makes of the gray candidates.

Where there is no ground truth, the pre-filter reports an *unlabeled estimate* of its own error rates
(`LinkageAnalysis.ErrorRates`: the false-match and false-non-match rates the fitted EM mixture expects of
its own calls, with the preconditions of that estimate flagged when they did not hold). An estimate is not
a measurement: it says whether a clerical review of a sample is worth running, not what it would find.

The unit such a review is scored on is fixed as B-cubed per record, not per pair
(`ClusteringMetrics.BCubed`), and the sampling and adjudication design is
[clerical-review.md](clerical-review.md), whose estimator is in the library as `ClericalReviewEstimator`;
the path from a reviewer's verdicts to the per-record scores it consumes is `ClericalReviewScoring`. The
arithmetic that turns a reviewed sample into a population score with an interval exists end to end and has
been run with a benchmark's labels as the reviewer — which showed a pilot's interval collapsing where
errors are rare, since fixed so a stratum that shows no error keeps the uncertainty its sample allows.
What is still missing is a review by people on a corpus of the domains above.

## Field comparison

Field comparison inside the pre-filter is exact-match (case-insensitive, ignoring surrounding whitespace
and Unicode form — decomposed Hangul, full-width digits) by default;
`LinkageOptions.Comparison = FieldComparison.JaroWinkler()` opts into a Jaro-Winkler threshold instead, so two records
denoting the same entity but differing only in notation (punctuation, spacing) still register as agreeing
on that field. On the entity-matching benchmarks its ratios separated matches better than exact comparison's
on nine of thirteen sets and worse on two (Walmart-Amazon: 0.66 → 0.45 best single threshold), so the
default is unchanged. `FieldComparison.TokenJaccard` and `FieldComparison.MongeElkan` read a value as
words instead, for values whose words differ in order or number: on the same benchmarks token Jaccard
separated best on seven of twelve sets (product titles, values moved between columns) and whole-value
comparison stayed ahead on clean bibliographic ones, so no method replaces the default
([linkage-benchmark.md](linkage-benchmark.md)); `LinkageOptions.FieldComparisons` sets a method per
field, for tables that hold both kinds of value. A comparison with several thresholds
(`FieldComparison.JaroWinkler(1.0, 0.92, 0.88)`) sorts pairs into levels, each fitted its own weight: for
Jaro-Winkler that raised alone F1 on nine of the twelve entity-matching sets (mean 0.427 → 0.474) and
shrank the gray zone; for token Jaccard it improved separation but not the result at the default
thresholds (mean 0.523 → 0.510), so levels stay a per-comparison choice and the default stays exact.

A field the caller declared as a reference (an inspection log's machine tag) is not compared at all for
records whose subject is known (`RawRecord.Subject`): it names another entity, so agreeing on it is not
evidence that two records are one. A field that names an owner — a `Child` relation's field, or a
`Parent` relation's when a child is declared on its own (rows of a form's repeated section) — also bounds
which pairs exist: records of two owners are never compared, since two rows of different sheets are two
rows whatever their cells say. A declared key (`DeclaredStructure.KeyField` — a table's key, a register's
number) does the same for the subject's own rows: two records of it with different keys are never compared
or fitted on. Without it, a nine-record batch with no true duplicate had the fit read a quantity and a note
two work orders happened to share as identity, and pre-linked them as one entity.
