# Linkage accuracy on a labeled benchmark

What `LinkagePipeline` achieves on public data whose true clusters are known, and what the
clerical review protocol ([clerical-review.md](clerical-review.md)) says about the same runs when
the labels play the reviewer. The numbers are properties of these datasets and configurations
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
| exact, **without date of birth and identity number** | 0.992 / 0.997 / 0.994 | 0.986 / 0.993 / 0.990 |
| Jaro-Winkler ≥ 0.90, without them | 1.000 / 1.000 / 1.000 | 1.000 / 0.999 / 0.999 |

With a date of birth and an identity number on every record the sets are close to a ceiling. Names
and addresses alone — the common real case — are where exact comparison starts to merge and split
wrongly, and where the similarity comparator (`LinkageOptions.UseStringSimilarityComparator`)
recovers nearly all of it.

## The review protocol, checked against the census

For each configuration the protocol was run as written — a pilot of 30 records per stratum
(`ClericalReviewSampler.DrawPilot`), per-record scoring (`ClericalReviewScoring.Score`), the
stratified estimate (`ClericalReviewEstimator.Estimate`) — with the labels as the reviewer, over
100 seeds, counting how often the 95% interval contains the census value:

| configuration | set | precision covered | recall covered |
|---|---|---|---|
| exact, without identifiers | `dataset1` | 81/100 | 57/100 |
| exact, without identifiers | `dataset3` subset | 85/100 | 77–79/100 |
| Jaro-Winkler, without identifiers | `dataset3` subset | 100/100 | **4/100** |
| every other configuration | both | 100/100 | 100/100 |

**A pilot's interval is not a 95% interval where errors are rare.** When no sampled record in a
stratum is wrong, that stratum's spread is zero and the interval collapses around a perfect
score — the Jaro-Winkler row above misses a recall of 0.9987 ninety-six times in a hundred with an
interval of zero width. Every one of these estimates carried a caveat
(`NoVariationInStratum`, `StratumIsCensus`) and none was `IsReliable`: the estimator says it is
not to be trusted, and this table is what that warning costs when it is ignored. The protocol's
remedy is the round after the pilot (clerical-review.md §2), not a wider reading of the pilot.

## The unlabeled estimate, checked against the labels

The pre-filter's own error-rate estimate (`LinkageErrorRateEstimate`, no labels) next to the pair
rates the labels give, for the configurations where errors occur:

| configuration | set | false-match: estimated / labeled | false-non-match: estimated / labeled |
|---|---|---|---|
| exact, without identifiers | `dataset1` | 0.00002 / 0.00002 | 0.00498 / 0.00000 |
| exact, without identifiers | `dataset3` subset | 0.00010 / 0.00004 | 0.00509 / 0.00287 |

It found the false-match rate's order of magnitude and overstated the false-non-match rate.

## Reproducing

```
dotnet test tests/Eyu.Core.Tests -p:IncludeEyuBenchmarks=true --filter-class Eyu.Core.Tests.Benchmark.FebrlLinkageMeasurement
```

`EYU_BENCHMARK_REPORT=<file>` writes the full report — the fitted parameters, strata sizes, linkage
rate, threshold sensitivity and pairwise figures per configuration. A run takes about twenty
minutes; it needs the network the first time (`EYU_BENCHMARK_CACHE` keeps the files).
