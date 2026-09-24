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
its configuration rather than from a reviewer's judgment. Assigned by
`ClericalReviewSampler.StratifyRecords`, which reads the posterior band
(`LinkageErrorRateEstimator.IsAmbiguous`) rather than `LinkageClassification.GrayZone` — the
classification is a threshold on the ratio and drops pairs a chain of matches already resolved,
while the stratum wants every record the fitted model was unsure about, resolved or not.

**Allocation.** Sample every stratum, with unequal fractions: S3 heaviest, S1 lightest. With no
prior variance, a pilot of 30–50 reviewed records per stratum is enough to size the next round;
after that, allocate proportionally to `N_h · s_h` (Neyman) using the pilot's own per-stratum
standard deviations. Never let a stratum go unsampled to save budget — an unsampled stratum has
no estimate, and its weight does not disappear from the population.

`ClericalReviewSampler.DrawPilot` draws the pilot (seeded, without replacement, a stratum smaller
than the request taken whole rather than skipped); `AllocateNeyman` sizes the round after it from
the pilot's per-stratum deviations, with a floor of one record per stratum for the same reason.
Two design-stage rules keep that arithmetic from planning against what a small pilot merely
failed to see: a pilot deviation is read no lower than that of a score failing one time in
twenty (`MinimumDesignDeviation`, `sqrt(0.05 · 0.95)`), because a stratum whose pilot records
all scored 1.0 has not measured its spread but bounded it — planned against literally, the
population's largest stratum gets one record in the next round; and a stratum that would be
allocated more records than it holds is capped there and its surplus handed to the strata that
can still take records, in the same proportions, so the review budget is spent rather than
silently returned short.

---

## 3. From reviewed records back to a population number

Let stratum *h* hold `N_h` records, of which `n_h` were reviewed, and let `x̄_h` be the mean of a
per-record score (precision or recall) over those `n_h`. With `N = Σ N_h`:

```
estimate        x̄  = Σ_h (N_h / N) · x̄_h
standard error  SE = sqrt( Σ_h (N_h / N)² · (1 − n_h/N_h) · s²_h / n_h )
interval        x̄ ± 1.96 · SE          (95%, normal approximation)
```

Computed by `ClericalReviewEstimator.Estimate`, over per-record scores. Those come from
`ClericalReviewScoring`, which composes each sampled record's true cluster from the reviewer's
verdicts alone — the predicted cluster minus the co-members judged *different*, plus the outside
candidates judged *same* (Binette et al. 2024, §3.2) — and scores the record against it. No
reference clustering of the population is needed or wanted: a sampled review never has one, and
assembling one by leaving unreviewed records where the system put them scores a false merge's
unreviewed partners as if they were right. (`ClusteringMetrics.BCubedPerRecord` is the census
form of the same per-record score, for the case where the whole truth is known.) Records are
composed one at a time; two sampled records in one predicted cluster can be given true clusters
that contradict each other, and that is reported as reviewer disagreement (§4), not reconciled.
Both intervals come back on one `ClericalReviewEstimate`, unclamped, with the assumptions that
did not hold reported as caveats rather than as silence.

A worked example, small enough to check by hand (it is pinned as a test): sixteen records, ten
true entities, and a system that merged a namesake *j1* into a three-record cluster *a* on
ambiguous evidence. Stratifying gives five contested records, eight clean merges, three
singletons. A pilot of three per stratum draws *a1*, *a3*, *j1* from the contested stratum. The
reviewer rejects *j1* for *a1* and *a3* (precision 3/4 each) and rejects all three *a* records for
*j1* (precision 1/4); every other sampled record scores 1. The estimate is
`5/16 · 7/12 + 8/16 · 1 + 3/16 · 1 ≈ 0.870`, beside the 0.906 a census against the full truth
would give. The same verdicts pushed through a patched reference clustering — unreviewed records
left where the system put them — came out at 0.568, which is the number this section exists to
prevent.

Two cautions that decide whether the interval means anything:

- **The scores are bounded and skewed.** In S1 and S2 most records score exactly 1.0, so the
  normal approximation is poor near the boundary. Report a **bootstrap percentile interval**
  alongside it, and prefer the bootstrap when the two disagree. The bootstrap is the rescaling
  bootstrap of Rao and Wu (1988) for stratified sampling without replacement: within each stratum,
  `n_h − 1` draws with replacement, the resampled mean pulled toward the observed one by
  `sqrt(1 − n_h/N_h)`, 2000 replicates of `x̄`. That rescaling is what makes the two intervals
  comparable — it carries the same finite-population correction the standard error carries, so a
  stratum reviewed in full contributes no spread to either, and where the intervals still differ
  it is the skew, not the method.
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

- **Who adjudicates.** A score that will be published — in a README, a report, a claim to a
  consumer — rests on ground truth: people applying these rules, or the labels of a benchmark
  that ships them. A model may pilot the instructions or act as the second reviewer below, and
  when it does the report's reviewer line says so; its verdicts are not the true clusters of a
  published score. The reason is the one in *Why this exists* one step removed: a model's errors
  are not independent of the kind of errors a linkage makes, so a model reviewer can agree with a
  mistake for the same reason the system made it.
- **Blind.** The reviewer sees candidates, not verdicts, posteriors, or cluster boundaries as the
  system drew them. A reviewer shown the answer agrees with it.
- **Three outcomes, never two.** *Same entity* · *different entity* · *cannot tell from the
  record*. The third is a finding about the data, not a failure to decide; it is reported as its
  own rate and never silently folded into either side. A record whose cluster cannot be settled
  is excluded from the scored set and counted in the exclusion line, and the exclusion rate is
  reported next to the score. In code: any *cannot tell* verdict on a sampled record's candidates
  leaves that record unscored (`ScoredRecord.IsExcluded`, with the unresolved candidates named),
  and `ReviewScoring.Exclusions` carries the line per stratum. Exclusion is not missing at random
  — the records a reviewer cannot settle are where the errors concentrate — so the score is also
  **bounded**: each excluded record is put back at the worst and the best score its unresolved
  candidates could still give it (`ScoredRecord.LowerBound` / `UpperBound`), and estimating
  `ReviewScoring.PrecisionBounds` / `RecallBounds` gives the band the exclusions could move the
  population score within. Report the band next to the exclusion rate; a band that straddles the
  decision being made means the unresolved records have to be resolved, not counted.
- **Double-review a subsample.** 20% of each stratum goes to a second reviewer. Report Cohen's
  κ, pooled over every double-reviewed pair rather than per stratum — a stratum's subsample is a
  handful of verdicts and a coefficient over a handful says nothing. Report Gwet's AC1 (Gwet 2008)
  and per-outcome specific agreement beside it: in S1 and S2 nearly every verdict is the same
  outcome, and under that prevalence κ collapses toward zero (or is undefined) while the
  reviewers in fact agreed on almost everything — the κ-below-0.6 rule reads as failure exactly
  where the review is going well. Read κ where the outcomes are mixed and AC1 where they are
  not (`ClericalReviewAgreement.Measure`). A low coefficient where the outcomes *are* mixed means
  the interval understates the real uncertainty — the disagreement is between reviewers, and no
  amount of sampling fixes it. Fix the instructions and re-review before reporting a score.
  Scoring itself runs under one designated reviewer's verdicts (`ClericalReviewScoring.Score`);
  the second reviewer's are for this line, never averaged into the true clusters.
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
| Specification | the matching variables and how each is compared (`LinkageOptions`, the comparators in use), the blocking step or its absence, the thresholds, and the fitted `m`/`u` probabilities and match prior with their `EstimationStatus` — "the configuration" spelled out, so another run can be the same one |
| Sample | `n_h` per stratum, allocation rule, draw method and seed (the estimate carries the seed it used) |
| Reviewers | how many, the written adjudication instructions (and every rule added under §4), and what they were shown with |
| Scores | B-cubed precision and recall, each with its 95% interval (normal and bootstrap) — one `ClericalReviewEstimate` per measure |
| Linkage rate | records placed in a cluster of size ≥ 2 as a fraction of `N`, and the same per stratum — the number that decides how much of the data any downstream analysis actually sees |
| Differential error | precision and recall per subgroup that matters downstream (record source, period, a key attribute's presence), where the sample supports it — linkage error is not random, and an error concentrated in one subgroup biases whatever is computed on the linked set |
| Reviewer agreement | κ and Gwet's AC1 on the double-reviewed subsample, its size, and specific agreement per outcome |
| Exclusions | *cannot tell* rate per stratum, and the worst/best-case band the excluded records could move each score within |
| Threshold sensitivity | how the pre-filter's *unlabeled* error-rate estimate moves across neighbouring thresholds — never a re-scoring of this sample (see below) |
| Out of scope | blocking loss — measured separately, or stated as unmeasured |
| Comparison | the pre-filter's unlabeled estimate for the same run, side by side |
| Other metrics *(optional)* | pairwise precision/recall and cluster-level precision/recall for the same run, labelled as such and reported beside — never instead of — the B-cubed lines |

The specification, reviewer, linkage-rate, differential-error and sensitivity lines are the items
the reporting guidance for linked data asks for (GUILD, Gilbert et al. 2018; Harron et al. 2017):
what was linked on and how, who adjudicated and under which rules, how much of the data linked,
whether the errors fall evenly, and how fragile the result is to the thresholds. A review that
reports a score without them says what the number is and not what it is a number *of*.

**Threshold sensitivity is not measured on the sample.** §3 and §6 are firm that a change of
thresholds changes the strata and voids the design weights, so re-scoring the reviewed records
under another threshold produces a number with no design behind it. The sensitivity line is
therefore filled from the cheap side: the pre-filter's unlabeled estimate
(`LinkageErrorRateEstimate`) re-fitted at neighbouring thresholds, which costs nothing and needs no
sample. The comparison line says whether that estimate can be trusted on this data; if it can,
its movement across thresholds is the sensitivity; if it cannot, the line reports that, and a
different threshold means a different review.

**Other metrics are a side-by-side, not a replacement.** The scoring unit is B-cubed per record
(§1) and this table's estimates are built on that design; pairwise and cluster-level figures are
what most tooling reports and what a reader may want to compare against, so they may be shown for
the same run — but they are a census over the predicted clustering against whatever truth is
available, carry no interval from this design, and must be labelled as such.

The comparison line is the one worth running the review for: it says whether the cheap estimate
can be trusted on this kind of data, which is what every future run without a review depends on.

---

## 6. When not to run it

- **The estimate is unreliable for a reason a sample will not fix.** If the caveats report too
  few pairs or a failed EM fit, the clustering itself is not in a state worth measuring — fix the
  input, then sample.
- **The configuration is still moving.** Every threshold change costs the whole sample (§3).
- **The corpus is not the one you care about.** These numbers are properties of a dataset and a
  configuration together, not of the library. A review on one corpus says nothing about another;
  it says what a review *costs* and whether the cheap estimate tracked it.
