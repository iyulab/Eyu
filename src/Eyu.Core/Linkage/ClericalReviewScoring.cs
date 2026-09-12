namespace Eyu.Core.Linkage;

/// <summary>
/// What a reviewer concluded about one candidate shown next to a sampled record — the three
/// outcomes of <c>docs/clerical-review.md</c> section 4, never two. <see cref="CannotTell"/> is a
/// finding about the data, not a failure to decide, and it is never folded into either side.
/// </summary>
public enum ReviewOutcome
{
    /// <summary>The candidate and the sampled record are the same entity.</summary>
    Same,

    /// <summary>The candidate and the sampled record are different entities.</summary>
    Different,

    /// <summary>The record does not carry enough to settle it either way.</summary>
    CannotTell,
}

/// <summary>
/// One reviewer's verdict on one candidate for one sampled record. A review produces one of these
/// per candidate the reviewer was shown; a double-reviewed record produces two per candidate, from
/// two reviewers, and those are compared by <see cref="ClericalReviewAgreement"/> rather than
/// merged.
/// </summary>
public sealed record ReviewVerdict(string RecordId, string CandidateId, ReviewOutcome Outcome, string ReviewerId);

/// <summary>
/// One sampled record after its verdicts were composed into a true cluster and scored.
/// <para>
/// <paramref name="Score"/> is null when the record was excluded: at least one candidate came
/// back <see cref="ReviewOutcome.CannotTell"/>, so the true cluster cannot be settled and the
/// protocol keeps the record out of the scored set and counts it in the exclusion line instead.
/// <paramref name="UnresolvedCandidateIds"/> names those candidates, and
/// <paramref name="LowerBound"/> / <paramref name="UpperBound"/> are the scores the record would
/// get if every unresolved candidate went the worst way and the best way: for precision, an
/// unresolved co-member counted as different, then as the same; for recall, an unresolved
/// co-member counted as different and an unresolved outsider as a missed member, then the
/// reverse. Exclusion is informative — the records a reviewer cannot settle are where the errors
/// concentrate — so the bounds say what the exclusion line could be hiding. A resolved record's
/// bounds are both its score.
/// </para>
/// <para>
/// <paramref name="FalseMergeIds"/> is the reviewer's <c>A_r</c> — predicted co-members judged
/// <see cref="ReviewOutcome.Different"/> — and <paramref name="MissedMemberIds"/> the reviewer's
/// <c>B_r</c> — candidates outside the predicted cluster judged <see cref="ReviewOutcome.Same"/>
/// (Binette et al. 2024, section 3.2). Both are filled from the verdicts that did resolve, whether
/// or not the record was excluded.
/// </para>
/// </summary>
public sealed record ScoredRecord(
    string RecordId,
    RecordScore? Score,
    RecordScore LowerBound,
    RecordScore UpperBound,
    IReadOnlyList<string> FalseMergeIds,
    IReadOnlyList<string> MissedMemberIds,
    IReadOnlyList<string> UnresolvedCandidateIds)
{
    /// <summary>True when the reviewer could not settle the record's cluster and it was left unscored.</summary>
    public bool IsExcluded => Score is null;
}

/// <summary>
/// The scored strata with every excluded record put back at its worst-case score
/// (<paramref name="Lower"/>) and at its best-case score (<paramref name="Upper"/>). Estimating
/// each gives the band the exclusions could move the population score within; a band that
/// straddles the decision being made is the sign that the unresolved records must be resolved,
/// not just counted.
/// </summary>
public sealed record StrataBounds(IReadOnlyList<ReviewStratum> Lower, IReadOnlyList<ReviewStratum> Upper);

/// <summary>
/// The exclusion line of one stratum: how many of its sampled records were reviewed, and how many
/// of those the reviewer could not settle. The rate is reported next to the score, per the
/// protocol, because a stratum that lost half its sample to <see cref="ReviewOutcome.CannotTell"/>
/// has an interval that describes the other half.
/// </summary>
public sealed record StratumExclusion(ReviewStratumKind Kind, int Reviewed, int Excluded)
{
    /// <summary>Excluded as a fraction of reviewed; zero when nothing was reviewed.</summary>
    public double Rate => Reviewed == 0 ? 0.0 : (double)Excluded / Reviewed;
}

/// <summary>
/// A reviewed sample scored and shaped for <see cref="ClericalReviewEstimator.Estimate"/>: one
/// <see cref="ReviewStratum"/> list per measure, because precision and recall are estimated
/// separately, plus the exclusion line per stratum and every record's own result.
/// <para>
/// A stratum whose every reviewed record was excluded comes back with an empty score list. That is
/// a legitimate outcome of the data, not a defect here, but the estimator refuses it — the stratum
/// then has no estimate and its weight does not leave the population, so the protocol's answer is
/// to review more of it, not to drop it.
/// </para>
/// </summary>
public sealed record ReviewScoring(
    IReadOnlyList<ReviewStratum> Precision,
    IReadOnlyList<ReviewStratum> Recall,
    StrataBounds PrecisionBounds,
    StrataBounds RecallBounds,
    IReadOnlyList<StratumExclusion> Exclusions,
    IReadOnlyList<ScoredRecord> Records);

/// <summary>
/// Turns a reviewer's verdicts on a sampled record into that record's B-cubed score — the link
/// between section 1 of <c>docs/clerical-review.md</c>, where the reviewer establishes a sampled
/// record's true cluster, and section 3, where per-record scores become a population estimate.
/// <para>
/// The composition follows Binette et al. 2024, section 3.2, one record at a time: the true
/// cluster is the predicted cluster minus the co-members judged different, plus the outside
/// candidates judged the same. Precision and recall then need only that one true cluster and the
/// record's predicted cluster — no reference clustering of the whole population, which a sampled
/// review never has. Assembling one by leaving unreviewed records where the system put them is
/// the workaround this exists to make unnecessary: it scores a false merge's unreviewed partner
/// as if it were right, and the number it produces is wrong without saying so.
/// </para>
/// <para>
/// Nothing is reconciled across records. Two sampled records in one predicted cluster can be given
/// true clusters that contradict each other; that is a finding about the reviewers, surfaced by
/// <see cref="ClericalReviewAgreement"/> when they were two, and not something a transitive
/// closure should paper over.
/// </para>
/// </summary>
public static class ClericalReviewScoring
{
    /// <summary>
    /// Scores every sampled record of <paramref name="sample"/> from one reviewer's verdicts and
    /// shapes the result for the estimator. The candidates a record's verdicts must cover are
    /// read from <paramref name="analysis"/> the way the protocol shows them to the reviewer:
    /// every other record in its predicted cluster, and every record it was ever compared with
    /// (<see cref="LinkageAnalysis.PairLinkages"/> — the pipeline compares every pair, so this is
    /// every other record in the batch, and recall is recall within that set).
    /// <para>
    /// <paramref name="reviewerId"/> designates whose verdicts score. Double review exists to
    /// measure agreement, not to be averaged away here; a caller with two reviewers scores under
    /// one of them and reports κ from the other.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// When the designated reviewer has no verdicts at all, or when any sampled record's verdicts
    /// fail the contract of <see cref="ScoreRecord"/>.
    /// </exception>
    public static ReviewScoring Score(
        LinkageAnalysis analysis,
        ReviewSample sample,
        IReadOnlyList<ReviewVerdict> verdicts,
        string reviewerId)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(verdicts);
        ArgumentException.ThrowIfNullOrEmpty(reviewerId);

        var byRecord = new Dictionary<string, List<ReviewVerdict>>(StringComparer.Ordinal);
        foreach (var verdict in verdicts)
        {
            if (!string.Equals(verdict.ReviewerId, reviewerId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!byRecord.TryGetValue(verdict.RecordId, out var list))
            {
                byRecord[verdict.RecordId] = list = [];
            }

            list.Add(verdict);
        }

        if (byRecord.Count == 0)
        {
            throw new ArgumentException($"Reviewer '{reviewerId}' has no verdicts in the list given.", nameof(verdicts));
        }

        var clusterOf = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var cluster in analysis.Clustering.Clusters)
        {
            foreach (var id in cluster.RecordIds)
            {
                clusterOf[id] = cluster.RecordIds;
            }
        }

        var candidatesOf = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var pair in analysis.PairLinkages)
        {
            Admit(candidatesOf, pair.RecordIdA, pair.RecordIdB);
            Admit(candidatesOf, pair.RecordIdB, pair.RecordIdA);
        }

        var precision = new List<ReviewStratum>(sample.Strata.Count);
        var recall = new List<ReviewStratum>(sample.Strata.Count);
        var precisionLower = new List<ReviewStratum>(sample.Strata.Count);
        var precisionUpper = new List<ReviewStratum>(sample.Strata.Count);
        var recallLower = new List<ReviewStratum>(sample.Strata.Count);
        var recallUpper = new List<ReviewStratum>(sample.Strata.Count);
        var exclusions = new List<StratumExclusion>(sample.Strata.Count);
        var records = new List<ScoredRecord>();
        foreach (var stratum in sample.Strata)
        {
            var count = stratum.SelectedRecordIds.Count;
            var precisions = new List<double>(count);
            var recalls = new List<double>(count);
            var pLow = new List<double>(count);
            var pHigh = new List<double>(count);
            var rLow = new List<double>(count);
            var rHigh = new List<double>(count);
            var excluded = 0;
            foreach (var id in stratum.SelectedRecordIds)
            {
                if (!clusterOf.TryGetValue(id, out var predicted))
                {
                    throw new ArgumentException($"Sampled record '{id}' is not in any cluster of the analysis.", nameof(sample));
                }

                var candidates = candidatesOf.TryGetValue(id, out var admitted) ? admitted : [];
                var scored = ScoreRecord(id, predicted, candidates, byRecord.TryGetValue(id, out var own) ? own : []);
                records.Add(scored);
                if (scored.Score is { } score)
                {
                    precisions.Add(score.Precision);
                    recalls.Add(score.Recall);
                }
                else
                {
                    excluded++;
                }

                pLow.Add(scored.LowerBound.Precision);
                pHigh.Add(scored.UpperBound.Precision);
                rLow.Add(scored.LowerBound.Recall);
                rHigh.Add(scored.UpperBound.Recall);
            }

            var name = stratum.Kind.ToString();
            precision.Add(new ReviewStratum(name, stratum.PopulationSize, precisions));
            recall.Add(new ReviewStratum(name, stratum.PopulationSize, recalls));
            precisionLower.Add(new ReviewStratum(name, stratum.PopulationSize, pLow));
            precisionUpper.Add(new ReviewStratum(name, stratum.PopulationSize, pHigh));
            recallLower.Add(new ReviewStratum(name, stratum.PopulationSize, rLow));
            recallUpper.Add(new ReviewStratum(name, stratum.PopulationSize, rHigh));
            exclusions.Add(new StratumExclusion(stratum.Kind, count, excluded));
        }

        return new ReviewScoring(
            precision,
            recall,
            new StrataBounds(precisionLower, precisionUpper),
            new StrataBounds(recallLower, recallUpper),
            exclusions,
            records);
    }

    /// <summary>
    /// Scores one sampled record from one reviewer's verdicts on it. <paramref name="candidates"/>
    /// are the records the reviewer was shown besides the predicted co-members; together with
    /// <paramref name="predictedCluster"/> minus the record itself they are the set every one of
    /// which needs a verdict.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// When <paramref name="recordId"/> is not in its own predicted cluster; when a required
    /// candidate has no verdict (named, so the reviewer can be asked); when a verdict names a
    /// candidate the reviewer was never shown; when one candidate carries two verdicts; or when the
    /// verdicts come from more than one reviewer — the caller designates which reviewer scores,
    /// because averaging two reviewers into one true cluster would hide exactly the disagreement
    /// the protocol measures separately.
    /// </exception>
    public static ScoredRecord ScoreRecord(
        string recordId,
        IReadOnlyCollection<string> predictedCluster,
        IReadOnlyCollection<string> candidates,
        IReadOnlyList<ReviewVerdict> verdicts)
    {
        ArgumentException.ThrowIfNullOrEmpty(recordId);
        ArgumentNullException.ThrowIfNull(predictedCluster);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(verdicts);

        var predicted = new HashSet<string>(predictedCluster, StringComparer.Ordinal);
        if (!predicted.Contains(recordId))
        {
            throw new ArgumentException($"Record '{recordId}' is not a member of the predicted cluster given for it.", nameof(predictedCluster));
        }

        var required = new HashSet<string>(predicted, StringComparer.Ordinal);
        required.UnionWith(candidates);
        required.Remove(recordId);

        var outcomeOf = new Dictionary<string, ReviewOutcome>(StringComparer.Ordinal);
        string? reviewer = null;
        foreach (var verdict in verdicts)
        {
            if (!string.Equals(verdict.RecordId, recordId, StringComparison.Ordinal))
            {
                throw new ArgumentException($"A verdict for record '{verdict.RecordId}' was given while scoring '{recordId}'.", nameof(verdicts));
            }

            reviewer ??= verdict.ReviewerId;
            if (!string.Equals(reviewer, verdict.ReviewerId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Verdicts on '{recordId}' come from more than one reviewer ('{reviewer}', '{verdict.ReviewerId}'). Score under one reviewer and measure the other with {nameof(ClericalReviewAgreement)}.",
                    nameof(verdicts));
            }

            if (!required.Contains(verdict.CandidateId))
            {
                throw new ArgumentException($"Verdict on '{verdict.CandidateId}' for record '{recordId}': that candidate was never shown for it.", nameof(verdicts));
            }

            if (!outcomeOf.TryAdd(verdict.CandidateId, verdict.Outcome))
            {
                throw new ArgumentException($"Candidate '{verdict.CandidateId}' carries more than one verdict for record '{recordId}'.", nameof(verdicts));
            }
        }

        var missing = required.Where(id => !outcomeOf.ContainsKey(id)).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"Record '{recordId}' has no verdict on: {string.Join(", ", missing)}. Every predicted co-member and every admitted candidate needs one.",
                nameof(verdicts));
        }

        var falseMerges = new List<string>();
        var missedMembers = new List<string>();
        var unresolved = new List<string>();
        var unresolvedInside = 0;
        foreach (var (candidate, outcome) in outcomeOf.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            switch (outcome)
            {
                case ReviewOutcome.CannotTell:
                    unresolved.Add(candidate);
                    if (predicted.Contains(candidate))
                    {
                        unresolvedInside++;
                    }

                    break;
                case ReviewOutcome.Different when predicted.Contains(candidate):
                    falseMerges.Add(candidate);
                    break;
                case ReviewOutcome.Same when !predicted.Contains(candidate):
                    missedMembers.Add(candidate);
                    break;
                default:
                    break; // Same inside the cluster, or Different outside it: the system was right.
            }
        }

        // c(r) = ĉ(r) \ A_r ∪ B_r, and then section 1's two ratios over it. The shared part is
        // the predicted cluster minus the false merges; an unresolved co-member sits on neither
        // side until the bounds below send it to one.
        var unresolvedOutside = unresolved.Count - unresolvedInside;
        var sharedResolved = predicted.Count - falseMerges.Count - unresolvedInside;
        var lower = new RecordScore(
            (double)sharedResolved / predicted.Count,
            (double)sharedResolved / (sharedResolved + missedMembers.Count + unresolvedOutside));
        var upper = new RecordScore(
            (double)(sharedResolved + unresolvedInside) / predicted.Count,
            (double)(sharedResolved + unresolvedInside) / (sharedResolved + unresolvedInside + missedMembers.Count));

        if (unresolved.Count > 0)
        {
            return new ScoredRecord(recordId, null, lower, upper, falseMerges, missedMembers, unresolved);
        }

        return new ScoredRecord(recordId, lower, lower, upper, falseMerges, missedMembers, unresolved);
    }

    private static void Admit(Dictionary<string, HashSet<string>> candidatesOf, string record, string candidate)
    {
        if (!candidatesOf.TryGetValue(record, out var set))
        {
            candidatesOf[record] = set = new HashSet<string>(StringComparer.Ordinal);
        }

        set.Add(candidate);
    }
}
