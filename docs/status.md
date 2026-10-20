# Status

What has been measured about Eyu, what each measurement establishes, and what it does not. The
README carries the one-paragraph summary; this page carries the detail.

## In one line

Contract implemented; grounding integrity and response-parsing reliability validated against a real
model; entity-resolution accuracy measured on labeled public benchmarks (B-cubed F1 0.99–1.00 on
Febrl synthetic person records; pair-level F1 0.93–1.00 from the pre-filter alone on clean structured
entity-matching sets, with most pairs left to the model on product, dirty and long-text sets) and not
yet reviewed by people on domain data; exercised by one external package
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
field, for tables that hold both kinds of value.

A field the caller declared as a reference (an inspection log's machine tag) is not compared at all for
records whose subject is known (`RawRecord.Subject`): it names another entity, so agreeing on it is not
evidence that two records are one. A field that names an owner — a `Child` relation's field, or a
`Parent` relation's when a child is declared on its own (rows of a form's repeated section) — also bounds
which pairs exist: records of two owners are never compared, since two rows of different sheets are two
rows whatever their cells say.
