# Linkage accuracy on labeled benchmarks

Two kinds of public data: Febrl, whose true clusters are known for every record (the sections below), and
the entity-matching benchmarks of the DeepMatcher study, which label candidate pairs
([at the end](#entity-matching-benchmarks--pair-level-test-split)). For Febrl: what `LinkagePipeline` achieves, and what the
clerical review protocol ([clerical-review.md](clerical-review.md)) says about the same runs when
the labels play the reviewer. Measured with the estimator as of this release: the EM fits its own match prior and keeps a cap
on per-field evidence (see "The unlabeled estimate" below). The numbers are properties of these datasets and configurations
(clerical-review.md §6) — not of the library in general, and not of Korean text, document chunks
or free-form model output.

## Data

[Febrl](https://users.cecs.anu.edu.au/~Peter.Christen/Febrl/febrl-0.3/febrldoc-0.3/) synthetic person
records (given name, surname, street number, two address lines, suburb, postcode, state, date of
birth, an identity number), © 2002-2007 Australian National University and others, under the ANU
Open Source License 1.3. The files are not in this repository; the measurement downloads them from
a fixed commit of the `recordlinkage` package's copy and refuses them unless their SHA-256 matches.

| set | records | true entities | shape |
|---|---|---|---|
| `dataset1` | 1,000 | 500 | every entity two records: an original and one duplicate with one modified field |
| `dataset3`, entities numbered by a multiple of five | 1,037 | 400 | 159 alone, the rest two to six records; up to four modifications in a field and six in a record |

Blocking: none — every pair is compared (≈ 500,000 per set). Thresholds: the defaults (match
≥ 4, non-match ≤ −4). A blank value is never a disagreement.

## Census — the whole clustering against the whole truth

B-cubed per record (clerical-review.md §1), precision / recall / F1:

| configuration | `dataset1` | `dataset3` subset |
|---|---|---|
| exact comparison, all fields | 1.000 / 0.998 / 0.999 | 1.000 / 1.000 / 1.000 |
| Jaro-Winkler ≥ 0.90, all fields | 1.000 / 1.000 / 1.000 | 1.000 / 1.000 / 1.000 |
| exact, **without date of birth and identity number** | 0.994 / 0.994 / 0.994 | 0.990 / 0.992 / 0.991 |
| Jaro-Winkler ≥ 0.90, without them | 1.000 / 1.000 / 1.000 | 1.000 / 0.999 / 0.999 |

With a date of birth and an identity number on every record the sets are close to a ceiling. Names
and addresses alone — the common real case — are where exact comparison starts to merge and split
wrongly, and where the similarity comparator (`LinkageOptions.Comparison = FieldComparison.JaroWinkler()`)
recovers nearly all of it.

## The review protocol, checked against the census

For each configuration the protocol was run as written — a pilot of 30 records per stratum
(`ClericalReviewSampler.DrawPilot`), per-record scoring (`ClericalReviewScoring.Score`), the
stratified estimate (`ClericalReviewEstimator.Estimate`) — with the labels as the reviewer, over
100 seeds, counting how often the 95% interval contains the census value. Where the census is
1.000 in both measures every interval did, before and after; the rows below are the ones where
errors occur:

| configuration | set | precision: normal / bootstrap | recall: normal / bootstrap |
|---|---|---|---|
| exact, without identifiers | `dataset1` | **100** / 25 (normal was 81) | **100** / 100 (normal was 57) |
| exact, without identifiers | `dataset3` subset | **100** / 72 (normal was 85) | **100** / 92 (normal was 77) |
| Jaro-Winkler, without identifiers | `dataset3` subset | 100 / 100 | **100** / 28 (normal was 4) |

"Was" is the normal interval before the fix below and before the EM fitted its own match prior, over the same seeds.

**What was wrong, and what changed.** Errors here are rare, and a 30-record pilot from a stratum of
several hundred usually sees none. A stratum whose reviewed scores were all alike used to add zero
variance, so the interval collapsed onto a perfect score — the Jaro-Winkler row missed a recall of
0.9987 ninety-six times in a hundred with an interval of zero width. The estimator now gives such a
stratum the most variance its sample still allows (clerical-review.md §3), and every row's normal
interval contains the census. That coverage is conservative by construction: the bound is an upper
one. The bootstrap cannot resample a stratum with nothing in it to resample and still collapses,
which is why, while `NoVariationInStratum` is set, the normal interval is the one to read.

**What a pilot then says.** One pilot (seed 1) on `dataset1`, exact comparison, without identifiers:

| measure | census | pilot estimate | normal 95% interval | bootstrap | reviewed |
|---|---|---|---|---|---|
| precision | 0.9940 | 0.9990 | [0.8888, 1.1092] | [0.9990, 0.9990] | 52 of 1,000 |
| recall | 0.9940 | 0.9940 | [0.8838, 1.1042] | [0.9940, 0.9940] | 52 of 1,000 |

Both intervals run from about 0.88 past 1.0: no reviewed record in the largest stratum was wrong,
and 30 records cannot say more than that fewer than about one in eight might be. The bootstrap,
with nothing to resample there, is a point. That is the pilot doing its job — bounding the strata and sizing the next round
(clerical-review.md §2) — not the review's final number.

## The unlabeled estimate, checked against the labels

The pre-filter's own error-rate estimate (`LinkageErrorRateEstimate`, no labels) next to the pair
rates the labels give, for the configurations where errors occur:

| configuration | set | false-match: estimated / labeled | false-non-match: estimated / labeled |
|---|---|---|---|
| exact, without identifiers | `dataset1` | 0.00004 / 0.00001 | 0.00029 / 0.00000 |
| exact, without identifiers | `dataset3` subset | 0.00019 / 0.00002 | 0.00086 / 0.00430 |

Until the EM estimated its own match prior it held it at 0.01 — ten times the true share of
matching pairs here — and the false-non-match estimate was 0.00498 where the labels say 0. With
the prior fitted (0.00100 and 0.00233 here) the `dataset1` estimate is close to the labels; on the
`dataset3` subset it is still off by a factor of five in each direction. The estimate is only as
good as the model it is read from, and that model assumes the fields are independent: a suburb and
its postcode, or a street and its suburb, are not, and the per-field evidence cap
(`FellegiSunterEstimator.AgreementProbabilityFloor`) that keeps such correlated agreements from
outvoting everything else is itself a departure from the fitted mixture. Read it as the order of
magnitude, and as a reason to review, not as the rate.

## Which scale the thresholds are on

`LinkageOptions.MatchThreshold` and `NonMatchThreshold` (±4 by default) are compared against the
log-likelihood ratio alone, while a claim's confidence, the unlabeled error-rate estimate and the
review strata all read the posterior, which adds the logit of the fitted match prior (about −6 to
−7 on these sets). The same fitted pairs, classified once on each scale at the default ±4
(`Compare_threshold_scales_on_febrl_benchmarks`):

| configuration | set | scale | false match | false non-match | gray-zone pairs (true match / not) | F1 before adjudication | F1 with every gray-zone pair adjudicated correctly |
|---|---|---|---|---|---|---|---|
| exact | `dataset1` | ratio | 0 | 0 | 173 (2 / 171) | 0.9990 | 1.0000 |
| exact | `dataset1` | posterior | 0 | 2 | 7 (7 / 0) | 0.9955 | 0.9990 |
| exact, without identifiers | `dataset1` | ratio | 6 | 0 | 3036 (6 / 3030) | 0.9940 | 0.9970 |
| exact, without identifiers | `dataset1` | posterior | 0 | 3 | 74 (53 / 21) | 0.9712 | 0.9985 |
| exact | `dataset3` subset | ratio | 0 | 0 | 853 (18 / 835) | 1.0000 | 1.0000 |
| exact | `dataset3` subset | posterior | 0 | 7 | 76 (75 / 1) | 0.9993 | 1.0000 |
| exact, without identifiers | `dataset3` subset | ratio | 11 | 6 | 7558 (52 / 7506) | 0.9910 | 0.9950 |
| exact, without identifiers | `dataset3` subset | posterior | 0 | 41 | 272 (246 / 26) | 0.9808 | 0.9981 |
| Jaro-Winkler, without identifiers | `dataset3` subset | ratio | 0 | 0 | 607 (6 / 601) | 0.9994 | 1.0000 |
| Jaro-Winkler, without identifiers | `dataset3` subset | posterior | 0 | 3 | 77 (64 / 13) | 0.9964 | 1.0000 |

The other three configurations (Jaro-Winkler with identifiers, and on `dataset1` without) differ by
at most one pair in errors outside the gray zone. Over all eight, the ratio scale makes 23 errors outside the gray zone (17 false
matches, 6 false non-matches) and the posterior scale 57 (all false non-matches) — errors that no
adjudication can undo, because only gray-zone pairs reach the model. What the posterior scale buys
is the gray zone: 4 to 40 times fewer pairs (25 or more in half the configurations) put to the model, most of them true matches rather than
obvious non-matches. Two limits on reading this: at a fixed ±4 the posterior scale is the same
evidence held to a higher bar (roughly +3 to +11 on the ratio here), so the table compares two
operating points as much as two scales; and every pair past the ratio threshold with a posterior
near 5% was still correct here, because the true matches' ratios sit far above 4 and the per-field
evidence cap keeps a single agreement from reaching it. The thresholds stay on the ratio.

## Reproducing

```
dotnet test tests/Eyu.Core.Tests -p:IncludeEyuBenchmarks=true --filter-class Eyu.Core.Tests.Benchmark.FebrlLinkageMeasurement
```

`EYU_BENCHMARK_REPORT=<file>` writes the full report — the fitted parameters, strata sizes, linkage
rate, threshold sensitivity and pairwise figures per configuration. A run takes about twenty
minutes, half of it re-fitting the model at neighbouring thresholds (each report ends with a
"Run cost" line per configuration); it needs the network the first time (`EYU_BENCHMARK_CACHE` keeps the files).
The scale comparison above is a separate test in the same class and takes under two minutes on its
own (`--filter-method "*Compare_threshold_scales*"`).

## One new record against a known population

The use above links a batch. Asking whether *one* new record is someone already known scores it against
the known people with parameters measured once on them (`EstimateNonMatchAgreement`, `FromNonMatchAgreement`
with m = 0.92), as in the README. From `dataset3`'s 2,000 originals, `N` drawn at random are the known
people; up to 300 of their duplicates come back, and 300 originals left out arrive as strangers. Rows vary
the fields (all of them, or only the names, suburb and state), the surnames (Febrl's own, or redrawn per
person from the published shares of Korean surnames — 2015 census: Kim 21.5%, Lee 14.7%, Park 8.4%, the top
ten 63.9%), and whether value frequencies are used on the name and suburb fields. Exact comparison, no
blocking.

| fields | surnames | value frequencies | N known | returning: Match / Gray / not listed | wrong Gray per returning record: mean · p95 · max | wrong Match (total) | strangers with a Match (of which every Match shares the full name) | stranger Gray: mean · p95 · max |
|---|---|---|---|---|---|---|---|---|
| all | Febrl | no | 500 | 299 / 1 / 0 of 300 | 0.0 · 0 · 0 | 0 | 0 of 300 (0 same full name) | 0.0 · 0 · 0 |
| all | Febrl | no | 1500 | 297 / 2 / 1 of 300 | 0.0 · 0 · 0 | 0 | 0 of 300 (0 same full name) | 0.0 · 0 · 1 |
| all | Febrl | yes | 500 | 299 / 1 / 0 of 300 | 0.0 · 0 · 0 | 0 | 0 of 300 (0 same full name) | 0.0 · 0 · 0 |
| all | Febrl | yes | 1500 | 297 / 2 / 1 of 300 | 0.0 · 0 · 0 | 0 | 0 of 300 (0 same full name) | 0.0 · 0 · 1 |
| all | Korean shares | no | 500 | 298 / 2 / 0 of 300 | 0.0 · 0 · 1 | 0 | 0 of 300 (0 same full name) | 0.0 · 0 · 4 |
| all | Korean shares | no | 1500 | 296 / 3 / 1 of 300 | 0.0 · 0 · 3 | 0 | 0 of 300 (0 same full name) | 0.0 · 0 · 1 |
| all | Korean shares | yes | 500 | 299 / 1 / 0 of 300 | 0.0 · 0 · 1 | 0 | 0 of 300 (0 same full name) | 0.0 · 0 · 4 |
| all | Korean shares | yes | 1500 | 297 / 2 / 1 of 300 | 0.0 · 0 · 3 | 0 | 0 of 300 (0 same full name) | 0.0 · 0 · 1 |
| names and town | Febrl | no | 500 | 224 / 69 / 7 of 300 | 15.7 · 138 · 163 | 1 | 2 of 300 (1 same full name) | 14.2 · 67 · 168 |
| names and town | Febrl | no | 1500 | 227 / 65 / 8 of 300 | 40.8 · 147 · 491 | 11 | 7 of 300 (3 same full name) | 38.3 · 61 · 501 |
| names and town | Febrl | yes | 500 | 224 / 69 / 7 of 300 | 15.7 · 138 · 163 | 1 | 1 of 300 (0 same full name) | 14.2 · 67 · 168 |
| names and town | Febrl | yes | 1500 | 226 / 66 / 8 of 300 | 40.8 · 147 · 491 | 7 | 5 of 300 (2 same full name) | 38.3 · 61 · 501 |
| names and town | Korean shares | no | 500 | 259 / 40 / 1 of 300 | 39.8 · 198 · 229 | 30 | 34 of 300 (25 same full name) | 53.3 · 158 · 311 |
| names and town | Korean shares | no | 1500 | 275 / 25 / 0 of 300 | 104.7 · 269 · 910 | 79 | 90 of 300 (61 same full name) | 142.0 · 277 · 906 |
| names and town | Korean shares | yes | 500 | 258 / 41 / 1 of 300 | 38.5 · 198 · 215 | 32 | 32 of 300 (22 same full name) | 45.3 · 83 · 215 |
| names and town | Korean shares | yes | 1500 | 275 / 25 / 0 of 300 | 101.2 · 268 · 650 | 74 | 85 of 300 (56 same full name) | 125.8 · 258 · 664 |

- With an identifying field (date of birth, an identity number) the question is easy at every size.
- With names and a town only, the gray candidates a returning record brings grow with the known population,
  and with surnames as concentrated as in Korea strangers start to classify as `Match` — 30% of them at
  1,500 known people. Two in three of those share the known person's full name: those fields cannot tell
  them apart, and no weighting will.
- Value frequencies lower that only a little (90 → 85 strangers at 1,500; at 500 the wrong matches among
  returning records went from 30 to 32): most of what is left agrees on the full name, which they cannot
  discount. They are the standard correction for unevenly common values, kept as an
  opt-in; the lever that changes this table is a discriminating field.

Reproduce: `--filter-method "*Measure_scoring_new_records_against_a_known_population"` (about ten seconds).

## Entity-matching benchmarks — pair-level, test split

The thirteen benchmarks of the DeepMatcher study (Mudgal et al., *Deep Learning for Entity Matching: A
Design Space Exploration*, SIGMOD 2018), as the field scores them: two tables of one schema, each free of
duplicates, and labeled candidate pairs split into train, validation and test; the score is pair-level F1
on the test split. The candidate pairs are the benchmarks' own (they ship blocked), so what is measured is
the comparator, the estimator and the classifier, not the batch-wide pair generation of
`LinkagePipeline.Analyze`. The data is not in this repository and is not fetched: the measurement reads a
local copy of the published files, checked against a SHA-256 manifest, and reports counts and scores only.

Unsupervised, as Eyu runs: EM fits on every candidate pair's comparison vector (train, validation and
test, labels ignored), the default thresholds classify, and the labels score the test pairs only. Every
column but `id` is compared. Three readings, because the pre-filter is not a matcher that decides alone —
it passes the gray zone on to a model:

- **alone** — a gray-zone pair counts as no match;
- **gray share** — the pairs left to the model;
- **oracle** — the best single threshold on the ratio, chosen on the test labels: how well the ratios
  separate, never a result.

| set | test pairs (matches) | exact: alone F1 · gray · oracle | Jaro-Winkler ≥ 0.90: alone F1 · gray · oracle |
|---|---|---|---|
| Structured / Fodors-Zagats | 189 (22) | 0.977 · 0.01 · 1.000 | 1.000 · 0.02 · 1.000 |
| Structured / DBLP-ACM | 2,473 (444) | 0.934 · 0.16 · 0.943 | 0.954 · 0.15 · 0.966 |
| Structured / iTunes-Amazon | 109 (27) | 0.714 · 0.61 · 0.898 | 0.906 · 0.59 · 0.947 |
| Structured / DBLP-Scholar | 5,742 (1,070) | 0.224 · 0.98 · 0.685 | 0.725 · 0.60 · 0.826 |
| Structured / Beer | 91 (14) | 0.421 · 0.95 · 0.714 | 0.444 · 0.96 · 0.828 |
| Structured / Walmart-Amazon | 2,049 (193) | 0.135 · 0.99 · 0.660 | 0.248 · 0.98 · 0.452 |
| Structured / Amazon-Google | 2,293 (234) | 0.034 · 0.93 · 0.286 | 0.087 · 0.95 · 0.340 |
| Dirty / DBLP-ACM | 2,473 (444) | 0.013 · 0.94 · 0.399 | 0.268 · 0.87 · 0.750 |
| Dirty / DBLP-Scholar | 5,742 (1,070) | 0.118 · 0.90 · 0.349 | 0.316 · 0.95 · 0.659 |
| Dirty / iTunes-Amazon | 109 (27) | 0.121 · 0.93 · 0.475 | 0.121 · 0.94 · 0.444 |
| Dirty / Walmart-Amazon | 2,049 (193) | 0.229 · 0.98 · 0.266 | 0.050 · 0.99 · 0.328 |
| Textual / Abt-Buy | 1,916 (206) | 0.000 · 1.00 · 0.194 | 0.010 · 1.00 · 0.248 |
| Textual / Company | 22,503 (5,640) | 0.000 · 1.00 · 0.401 | 0.000 · 1.00 · 0.401 |

What it says:

- **Clean structured sets are decided by the pre-filter alone** (Fodors-Zagats, DBLP-ACM: 0.93–1.00),
  near what supervised matchers report on the same splits (98–100 F1 — Li et al., *Deep Entity Matching
  with Pre-Trained Language Models*, VLDB 2021, table 5).
- **Elsewhere most pairs are left to the model** (gray share 0.6–1.0), and the separating line sits inside
  the gray zone: the oracle thresholds of those sets fall between −1 and 3, against defaults of ±4. The defaults are
  conservative by design and are not tuned on these labels.
- **Whole-field comparison is the ceiling on product titles and long text.** Two titles of one product
  rarely agree as whole values, exactly or at Jaro-Winkler 0.90 (Amazon-Google oracle 0.29–0.34, Abt-Buy
  0.19–0.25); a single long `content` field carries no signal (Company: the oracle equals calling every
  pair a match). Dirty sets move values between columns, which column-aligned comparison cannot follow.
  Comparing values as words lifts part of that ceiling (below).
- **EM often does not converge on these candidate sets** (reported as `EstimationStatus.NotConverged`),
  and where it inflates the match prior (Walmart-Amazon, Jaro-Winkler: 0.31 against a true share of 0.09)
  precision falls with it — the same family as the unsupervised Fellegi-Sunter baseline (ECM) reported at
  0.01–0.09 F1 by Wu et al., *ZeroER*, SIGMOD 2020, table 3, on candidates of that paper's own blocking.
- **Pinning `u` to random pairs does not help here.** A second pair of configurations measures each field's
  `u` over random pairs of the two tables pooled and holds it while EM fits `m` and the prior. These
  candidate pairs were selected for looking alike, so their non-matches agree far more than random pairs
  do; EM puts the excess into the match component (fitted priors 0.86–0.99 where the true share is
  0.09–0.25) and precision collapses (Fodors-Zagats, Jaro-Winkler: 1.000 → 0.301 alone). The joint fit is
  the better one on such candidates.

### Comparing values as words

The same twelve sets (Company left out — see below) under the two word-token methods,
`FieldComparison.TokenJaccard(0.5)` (half the words shared) and `FieldComparison.MongeElkan()` (0.90), read
the same way. The settings are reported side by side, not tuned on these labels, and the default
comparison stays exact.

| set | token Jaccard ≥ 0.50: alone F1 · gray · oracle | Monge-Elkan ≥ 0.90: alone F1 · gray · oracle |
|---|---|---|
| Structured / Fodors-Zagats | 0.952 · 0.02 · 1.000 | 1.000 · 0.02 · 1.000 |
| Structured / DBLP-ACM | 0.922 · 0.20 · 0.936 | 0.894 · 0.08 · 0.947 |
| Structured / iTunes-Amazon | 0.875 · 0.39 · 0.945 | 0.868 · 0.26 · 0.929 |
| Structured / DBLP-Scholar | 0.785 · 0.22 · 0.823 | 0.771 · 0.17 · 0.799 |
| Structured / Beer | 0.815 · 0.37 · 0.933 | 0.667 · 0.89 · 0.828 |
| Structured / Walmart-Amazon | 0.401 · 0.93 · 0.588 | 0.393 · 0.96 · 0.530 |
| Structured / Amazon-Google | 0.136 · 0.99 · 0.494 | 0.073 · 0.96 · 0.481 |
| Dirty / DBLP-ACM | 0.707 · 0.69 · 0.775 | 0.613 · 0.83 · 0.702 |
| Dirty / DBLP-Scholar | 0.344 · 0.94 · 0.693 | 0.236 · 0.93 · 0.584 |
| Dirty / iTunes-Amazon | 0.200 · 0.97 · 0.581 | 0.000 · 0.99 · 0.526 |
| Dirty / Walmart-Amazon | 0.117 · 0.98 · 0.466 | 0.235 · 0.96 · 0.419 |
| Textual / Abt-Buy | 0.019 · 1.00 · 0.438 | 0.019 · 1.00 · 0.392 |

- **Where words move, words help.** Token Jaccard separates best of the four comparisons on seven of the
  twelve sets — the product sets (Amazon-Google oracle 0.34 → 0.49, Abt-Buy 0.25 → 0.44, Beer 0.83 → 0.93)
  and every Dirty set — and leaves less to the model on most of them (Beer 0.96 → 0.37 gray share,
  DBLP-Scholar 0.60 → 0.22).
- **Where values are clean, whole-value comparison stays ahead.** On DBLP-ACM, iTunes-Amazon and
  DBLP-Scholar Jaro-Winkler separates slightly better (0.966 against 0.936 on DBLP-ACM), and exact
  comparison stays best on Walmart-Amazon (0.660). No single method wins everywhere, so none replaces the
  default; a caller chooses by what its values look like, per field where a table holds both kinds
  (`LinkageOptions.FieldComparisons`).
- **Monge-Elkan sits between the two** and costs more — every word against every word, so Company's long
  text was not run under it.

### Comparing values in levels

A comparison with several thresholds sorts each pair into a level rather than agree/disagree, and EM fits
every level its own `m` and `u` — a near miss weighs between a match and a disagreement. The same twelve
sets, Jaro-Winkler on one line (0.90) against the top of a common name comparison in levels — equal,
≥ 0.92, ≥ 0.88, below (`FieldComparison.JaroWinkler(1.0, 0.92, 0.88)`) — and token Jaccard on one line
(0.50) against equal, ≥ 0.75, ≥ 0.50, below. Nothing was tuned on these labels.

| set | Jaro-Winkler ≥ 0.90: alone F1 · gray · oracle | levels 1.0 / 0.92 / 0.88: alone F1 · gray · oracle | token Jaccard ≥ 0.50 → levels 1.0 / 0.75 / 0.50: alone F1 · oracle |
|---|---|---|---|
| Structured / Beer | 0.444 · 0.96 · 0.828 | 0.571 · 0.76 · 0.839 | 0.815 → 0.750 · 0.933 → 0.933 |
| Structured / iTunes-Amazon | 0.906 · 0.59 · 0.947 | 0.920 · 0.10 · 1.000 | 0.875 → 0.941 · 0.945 → 1.000 |
| Structured / Fodors-Zagats | 1.000 · 0.02 · 1.000 | 1.000 · 0.01 · 1.000 | 0.952 → 1.000 · 1.000 → 1.000 |
| Structured / DBLP-ACM | 0.954 · 0.15 · 0.966 | 0.948 · 0.16 · 0.960 | 0.922 → 0.937 · 0.936 → 0.965 |
| Structured / DBLP-Scholar | 0.725 · 0.60 · 0.826 | 0.719 · 0.20 · 0.822 | 0.785 → 0.718 · 0.823 → 0.844 |
| Structured / Amazon-Google | 0.087 · 0.95 · 0.340 | 0.100 · 0.93 · 0.370 | 0.136 → 0.124 · 0.494 → 0.497 |
| Structured / Walmart-Amazon | 0.248 · 0.98 · 0.452 | 0.332 · 0.86 · 0.562 | 0.401 → 0.447 · 0.588 → 0.665 |
| Dirty / iTunes-Amazon | 0.121 · 0.94 · 0.444 | 0.258 · 0.70 · 0.491 | 0.200 → 0.258 · 0.581 → 0.500 |
| Dirty / DBLP-ACM | 0.268 · 0.87 · 0.750 | 0.384 · 0.77 · 0.844 | 0.707 → 0.467 · 0.775 → 0.779 |
| Dirty / DBLP-Scholar | 0.316 · 0.95 · 0.659 | 0.352 · 0.88 · 0.733 | 0.344 → 0.312 · 0.693 → 0.694 |
| Dirty / Walmart-Amazon | 0.050 · 0.99 · 0.328 | 0.070 · 0.88 · 0.324 | 0.117 → 0.152 · 0.466 → 0.472 |
| Textual / Abt-Buy | 0.010 · 1.00 · 0.248 | 0.029 · 0.85 · 0.300 | 0.019 → 0.019 · 0.438 → 0.442 |
| **mean of twelve** | 0.427 · — · 0.649 | 0.474 · — · 0.687 | 0.523 → 0.510 · 0.723 → 0.733 |

- **Levels help Jaro-Winkler.** Alone F1 rises on nine of the twelve sets and falls slightly on two
  (DBLP-ACM 0.954 → 0.948, DBLP-Scholar 0.725 → 0.719); the separation (oracle) rises on eight. The model's
  share shrinks with it: a near miss no longer sits at the same weight as a disagreement, so fewer pairs
  land between the thresholds (iTunes-Amazon 0.59 → 0.10 gray, DBLP-Scholar 0.60 → 0.20).
- **For token Jaccard the order improves but the default thresholds do not fit it.** Separation rises on
  ten sets (Walmart-Amazon 0.588 → 0.665), but alone F1 is mixed — up on six, down on five, with Dirty
  DBLP-ACM falling 0.707 → 0.467 — so the mean drops (0.523 → 0.510). Levels move the weights, and the fixed
  match and non-match thresholds then cut in a different place.
- **Where whole values already separate, levels add nothing.** On Febrl's two sets every Jaro-Winkler
  configuration, with or without levels, reaches a census F1 of 1.000 or 0.9994 (two of 1,394 duplicate
  pairs missed) — the same with levels as without, and no room to show a difference.
- The default comparison stays exact; levels are a choice per comparison, like the method.

Reproducing:

```
EYU_DEEPMATCHER_ROOT=<directory holding exp/<Type>/<Dataset>/exp_data/*.csv and SHA256SUMS> \
  dotnet test tests/Eyu.Core.Tests -p:IncludeEyuBenchmarks=true --filter-class Eyu.Core.Tests.Benchmark.DeepMatcherLinkageMeasurement
```

`EYU_DEEPMATCHER_DATASETS` (comma-separated, e.g. `Structured/DBLP-ACM`) selects sets. Twelve sets take
seconds; Company's long text under Jaro-Winkler takes about twenty minutes on one core.
