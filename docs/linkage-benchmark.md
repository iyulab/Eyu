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
100 seeds, counting how often the 95% interval contains the census value. Where the census is
1.000 in both measures every interval did, before and after; the rows below are the ones where
errors occur:

| configuration | set | precision: normal / bootstrap | recall: normal / bootstrap |
|---|---|---|---|
| exact, without identifiers | `dataset1` | **100** / 81 (was 81 / 81) | **100** / 57 (was 57 / 57) |
| exact, without identifiers | `dataset3` subset | **100** / 85 (was 85 / 85) | **100** / 79 (was 77 / 79) |
| Jaro-Winkler, without identifiers | `dataset3` subset | 100 / 100 | **100** / 4 (was 4 / 4) |

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
| precision | 0.9920 | 0.9800 | [0.9496, 1.0104] | [0.9462, 0.9971] | 65 of 1,000 |
| recall | 0.9970 | 0.9967 | [0.8903, 1.1030] | [0.9955, 0.9972] | 65 of 1,000 |

Recall's interval runs from 0.89 past 1.0: no reviewed record in the largest stratum was split
from its duplicate, and 30 records cannot say more than that fewer than about one in eight might
be. That is the pilot doing its job — bounding the strata and sizing the next round
(clerical-review.md §2) — not the review's final number.

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
