# Clerical Review Protocol (pilot)

How to turn the pre-filter's *unlabeled estimate* of its own error rates into a *measurement*,
without labeling a whole corpus: draw a stratified sample of records, have a person adjudicate
each one, and report a scored interval rather than a point.

This is a **pilot protocol**. It fixes the design decisions that have to be made before any
sample is drawn — what the unit is, how strata are formed, how the weights come back out, and
what the resulting number may and may not be called. It does not report results; a review that
runs under it reports its own.

> **Why this exists.** [`LinkageErrorRateEstimate`](../src/Eyu.Core/Linkage/LinkageErrorRateEstimate.cs)
> tells you what the fitted mixture *expects* of its own calls. That is an estimate from the same
> model that made the calls, so it cannot find an error the model is confidently wrong about. It
> says whether a review is worth running and where to point it. It is not the review.

---

## 1. The unit is a record, not a pair

The score is [B-cubed](../src/Eyu.Core/Linkage/ClusteringMetrics.cs), per record. That choice is
already fixed in code, and the sampling design follows from it rather than the other way round:

- **Sample records.** For a sampled record *r*, a reviewer establishes *r*'s true cluster, and
  per-record precision and recall are then computed exactly:
  `precision(r) = |predicted(r) ∩ true(r)| / |predicted(r)|`,
  `recall(r) = |predicted(r) ∩ true(r)| / |true(r)|`.
  Each sampled record yields two numbers in [0, 1] — no pair-level bookkeeping survives into the
  estimate.
- **Do not sample pairs.** A pair sample estimates pairwise precision/recall, which weights a
  cluster by the square of its size and is not the quantity being reported. Converting a pair
  sample into a B-cubed estimate afterwards requires the very clusters the sample did not draw.

**What a reviewer is shown**, for one sampled record: the record itself, every other record the
system placed in its cluster, and every remaining candidate the blocking step admitted for it.
Nothing about the system's verdict on individual pairs, and no posterior — see §4.

---

## 2. Strata

Errors are rare and concentrated; a simple random sample spends almost all of its budget on
records nothing was ever going to get wrong. Three strata, formed **before** sampling, from
information the system already has:

| Stratum | Definition | Why it is separate |
|---|---|---|
| **S1 singleton** | predicted cluster of size 1, no candidate pair above the gray-zone floor | Dominates the population; near-certain precision 1.0, so it carries the recall risk only |
| **S2 clean merge** | predicted cluster of size ≥ 2, no pair in the gray zone | Where a false merge costs the most and is least visible |
| **S3 contested** | the record has at least one pair inside the gray zone (`AmbiguousPosteriorLow`..`High`) | Where the model already says it does not know — the highest error density per reviewed record |

The gray-zone bounds are the estimator's own constants, so a run's strata are reproducible from
its configuration rather than from a reviewer's judgment.

**Allocation.** Sample every stratum, with unequal fractions: S3 heaviest, S1 lightest. With no
prior variance, a pilot of 30–50 reviewed records per stratum is enough to size the next round;
after that, allocate proportionally to `N_h · s_h` (Neyman) using the pilot's own per-stratum
standard deviations. Never let a stratum go unsampled to save budget — an unsampled stratum has
no estimate, and its weight does not disappear from the population.

---

## 3. From reviewed records back to a population number

Let stratum *h* hold `N_h` records, of which `n_h` were reviewed, and let `x̄_h` be the mean of a
per-record score (precision or recall) over those `n_h`. With `N = Σ N_h`:

```
estimate        x̄  = Σ_h (N_h / N) · x̄_h
standard error  SE = sqrt( Σ_h (N_h / N)² · (1 − n_h/N_h) · s²_h / n_h )
interval        x̄ ± 1.96 · SE          (95%, normal approximation)
```

Two cautions that decide whether the interval means anything:

- **The scores are bounded and skewed.** In S1 and S2 most records score exactly 1.0, so the
  normal approximation is poor near the boundary. Report a **bootstrap percentile interval**
  (resample reviewed records within each stratum, recompute `x̄`, 2000 draws) alongside it, and
  prefer the bootstrap when the two disagree.
- **The weights are design weights, not fit weights.** `N_h / N` comes from the population the
  sample was drawn from — the corpus as the system clustered it. Re-running the pipeline with
  different thresholds changes the strata, which invalidates the weights; a new configuration
  needs a new sample, not a re-weighting of the old one.

**Blocking loss is outside this design.** A true match that blocking never admitted as a
candidate is invisible to the reviewer, so recall estimated here is recall *within the candidate
set*. Measure blocking loss separately — a small unblocked sample, or a construction where truth
is known — and report the two numbers apart. Silently adding them gives a recall that is neither.

---

## 4. Adjudication

- **Blind.** The reviewer sees candidates, not verdicts, posteriors, or cluster boundaries as the
  system drew them. A reviewer shown the answer agrees with it.
- **Three outcomes, never two.** *Same entity* · *different entity* · *cannot tell from the
  record*. The third is a finding about the data, not a failure to decide; it is reported as its
  own rate and never silently folded into either side. A record whose cluster cannot be settled
  is excluded from the scored set and counted in the exclusion line, and the exclusion rate is
  reported next to the score.
- **Double-review a subsample.** 20% of each stratum goes to a second reviewer. Report Cohen's
  κ. A κ below roughly 0.6 means the interval understates the real uncertainty — the disagreement
  is between reviewers, and no amount of sampling fixes it. Fix the instructions and re-review
  before reporting a score.
- **Write the rule down when it is invented.** Whenever adjudication needs a rule the protocol
  did not anticipate (initials, a merged organization, a renamed entity), record it and apply it
  to everything reviewed so far. A rule invented halfway through makes the first half a different
  measurement.

---

## 5. What a completed review reports

One table, and no prose that outruns it:

| Line | Content |
|---|---|
| Population | `N`, and `N_h` per stratum, with the configuration that produced them |
| Sample | `n_h` per stratum, allocation rule, draw method and seed |
| Scores | B-cubed precision and recall, each with its 95% interval (normal and bootstrap) |
| Reviewer agreement | κ on the double-reviewed subsample, and its size |
| Exclusions | *cannot tell* rate per stratum |
| Out of scope | blocking loss — measured separately, or stated as unmeasured |
| Comparison | the pre-filter's unlabeled estimate for the same run, side by side |

The last line is the one worth running the review for: it says whether the cheap estimate can be
trusted on this kind of data, which is what every future run without a review depends on.

---

## 6. When not to run it

- **The estimate is unreliable for a reason a sample will not fix.** If the caveats report too
  few pairs or a failed EM fit, the clustering itself is not in a state worth measuring — fix the
  input, then sample.
- **The configuration is still moving.** Every threshold change costs the whole sample (§3).
- **The corpus is not the one you care about.** These numbers are properties of a dataset and a
  configuration together, not of the library. A review on one corpus says nothing about another;
  it says what a review *costs* and whether the cheap estimate tracked it.
