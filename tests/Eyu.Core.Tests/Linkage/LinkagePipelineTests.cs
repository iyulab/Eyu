using Eyu.Core.Linkage;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class LinkagePipelineTests
{
    private static RawRecord Record(string id, params (string Key, string? Value)[] fields) =>
        new(id, fields.ToDictionary(f => f.Key, f => f.Value));

    [Fact]
    public void An_empty_batch_produces_no_clusters_and_no_pair_linkages()
    {
        var analysis = LinkagePipeline.Analyze([]);

        Assert.Empty(analysis.Clustering.Clusters);
        Assert.Empty(analysis.PairLinkages);
    }

    [Fact]
    public void Bad_options_are_refused_regardless_of_batch_size()
    {
        // The threshold check used to live in the classifier, which a one-record batch never
        // reaches -- so the same bad option threw for two records and passed for one.
        var options = new LinkageOptions(MatchThreshold: -4.0, NonMatchThreshold: 4.0);

        Assert.Throws<ArgumentException>(() => LinkagePipeline.Analyze([Record("rec-1", ("name", "Acme"))], options));
        Assert.Throws<ArgumentException>(() => LinkagePipeline.Analyze([Record("rec-1", ("name", "Acme")), Record("rec-2", ("name", "Acme"))], options));
    }

    [Fact]
    public void Duplicate_record_ids_are_refused_by_name()
    {
        var records = new[] { Record("rec-1", ("name", "Acme")), Record("rec-1", ("name", "Acme Inc")), Record("rec-2", ("name", "Other")) };

        var error = Assert.Throws<ArgumentException>(() => LinkagePipeline.Analyze(records));

        Assert.Contains("rec-1", error.Message);
        Assert.DoesNotContain("rec-2", error.Message);
    }

    private static RawRecord Chunk(string id, string title, string path, int page, string content) =>
        Record(id, ("content", content), ("title", title), ("path", path), ("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static RawRecord[] TwoDocumentsOfThreeChunksEach() =>
    [
        Chunk("a-1", "Company profile", "/docs/profile.pptx", 1, "Founded in 1998; key customers include A Electronics and B Bank."),
        Chunk("a-2", "Company profile", "/docs/profile.pptx", 2, "Partners: C Cloud, D Soft. In-house product E-Platform launched 2021."),
        Chunk("a-3", "Company profile", "/docs/profile.pptx", 3, "Revenue 32 billion, 180 staff. Head office in one city, branch in another."),
        Chunk("b-1", "Product overview", "/docs/product.pdf", 1, "E-Platform combines document Q&A and log analysis."),
        Chunk("b-2", "Product overview", "/docs/product.pdf", 2, "Awards: a ministerial prize in 2024."),
        Chunk("b-3", "Product overview", "/docs/product.pdf", 3, "Customer list: A Electronics, B Bank, F Pharma."),
    ];

    [Fact]
    public void Document_fragments_sharing_metadata_are_pre_linked_as_one_entity_per_document_by_default()
    {
        // The pre-filter's premise is that a record denotes one entity, so agreement is identity.
        // Chunks of one document agree on title and path and disagree on content and page; with
        // two documents in the batch the estimator has the contrast it needs and reads the
        // metadata agreement as a match. Measured before this test existed: every same-document
        // pair classified Match, one cluster per document. This pins that behaviour so the option
        // below is understood as necessary, not cosmetic.
        var analysis = LinkagePipeline.Analyze(TwoDocumentsOfThreeChunksEach());

        Assert.Equal(2, analysis.Clustering.Clusters.Count);
        Assert.Contains(analysis.Clustering.Clusters, c => c.RecordIds.Order().SequenceEqual(["a-1", "a-2", "a-3"]));
        Assert.Contains(analysis.Clustering.Clusters, c => c.RecordIds.Order().SequenceEqual(["b-1", "b-2", "b-3"]));
        Assert.All(analysis.PairLinkages.Where(p => p.RecordIdA[0] == p.RecordIdB[0]), p => Assert.Equal(LinkageClassification.Match, p.Classification));
    }

    [Fact]
    public void Records_that_do_not_denote_entities_are_not_compared_at_all()
    {
        var analysis = LinkagePipeline.Analyze(TwoDocumentsOfThreeChunksEach(), new LinkageOptions(RecordsDenoteEntities: false));

        Assert.Equal(6, analysis.Clustering.Clusters.Count);
        Assert.All(analysis.Clustering.Clusters, c => Assert.Single(c.RecordIds));
        Assert.Empty(analysis.Clustering.GrayZonePairs);
        Assert.Empty(analysis.PairLinkages);
        Assert.Null(analysis.Parameters);
        Assert.Null(analysis.ErrorRates);
    }

    [Fact]
    public void A_single_record_batch_produces_one_singleton_cluster_and_no_pair_linkages()
    {
        var analysis = LinkagePipeline.Analyze([Record("rec-1", ("name", "Acme"))]);

        var cluster = Assert.Single(analysis.Clustering.Clusters);
        Assert.Equal(["rec-1"], cluster.RecordIds);
        Assert.Empty(analysis.PairLinkages);
    }

    [Fact]
    public void Two_records_agreeing_on_every_field_produce_one_pair_linkage_and_are_classified()
    {
        var records = new[]
        {
            Record("rec-1", ("name", "Acme Corp"), ("city", "Springfield")),
            Record("rec-2", ("name", "Acme Corp"), ("city", "Springfield")),
        };

        var analysis = LinkagePipeline.Analyze(records);

        var pairLinkage = Assert.Single(analysis.PairLinkages);
        Assert.Equal(("rec-1", "rec-2"), (pairLinkage.RecordIdA, pairLinkage.RecordIdB));
        Assert.Equal(LinkageClassification.Match, pairLinkage.Classification);
    }

    [Fact]
    public void Three_records_produce_three_pair_linkages_one_per_combination()
    {
        var records = new[]
        {
            Record("rec-1", ("name", "Acme")),
            Record("rec-2", ("name", "Beta")),
            Record("rec-3", ("name", "Gamma")),
        };

        var analysis = LinkagePipeline.Analyze(records);

        Assert.Equal(3, analysis.PairLinkages.Count);
    }

    [Fact]
    public void An_empty_or_single_record_batch_has_no_Parameters()
    {
        Assert.Null(LinkagePipeline.Analyze([]).Parameters);
        Assert.Null(LinkagePipeline.Analyze([Record("rec-1", ("name", "Acme"))]).Parameters);
    }

    [Fact]
    public void A_multi_record_batch_exposes_the_parameters_used_to_classify_its_pairs()
    {
        var records = new[]
        {
            Record("rec-1", ("name", "Acme Corp"), ("city", "Springfield")),
            Record("rec-2", ("name", "Acme Corp"), ("city", "Springfield")),
        };

        var analysis = LinkagePipeline.Analyze(records);

        Assert.NotNull(analysis.Parameters);
        Assert.Equal(FellegiSunterEstimator.HeuristicDefaultMatchPrior, analysis.Parameters!.MatchPrior);
        Assert.Equal(EstimationStatus.HeuristicDefault, analysis.Parameters.Status);
    }

    [Fact]
    public void Custom_LinkageOptions_thresholds_change_classification()
    {
        // One shared field, both agree: LLR = ln 9 ~ 2.197 -- GrayZone under the default 4.0
        // match threshold, but Match under a lowered 2.0 threshold.
        var records = new[]
        {
            Record("rec-1", ("name", "Acme")),
            Record("rec-2", ("name", "Acme")),
        };

        var defaultAnalysis = LinkagePipeline.Analyze(records);
        var loweredThresholdAnalysis = LinkagePipeline.Analyze(records, new LinkageOptions(MatchThreshold: 2.0));

        Assert.Equal(LinkageClassification.GrayZone, Assert.Single(defaultAnalysis.PairLinkages).Classification);
        Assert.Equal(LinkageClassification.Match, Assert.Single(loweredThresholdAnalysis.PairLinkages).Classification);
    }

    [Fact]
    public void Custom_LinkageOptions_EM_tuning_values_reach_the_estimator()
    {
        // 4 records, 6 pairs (>= MinimumPairsForEmEstimation) with two fields whose agreement
        // pattern is genuinely mixed, so the very first EM M-step moves m/u measurably away from
        // their 0.9/0.1 starting point -- this is what makes maxIterations:1 with the default
        // tolerance reliably NOT converge, while the same maxIterations:1 with an enormous
        // tolerance trivially DOES converge (any first-step movement clears it). Both outcomes can
        // only differ if both LinkageOptions fields actually reach FellegiSunterEstimator.Estimate.
        var records = new[]
        {
            Record("r1", ("f1", "A"), ("f2", "X")),
            Record("r2", ("f1", "A"), ("f2", "Y")),
            Record("r3", ("f1", "B"), ("f2", "X")),
            Record("r4", ("f1", "B"), ("f2", "Y")),
        };

        var notConverged = LinkagePipeline.Analyze(records, new LinkageOptions(MaxIterations: 1));
        var converged = LinkagePipeline.Analyze(records, new LinkageOptions(MaxIterations: 1, ConvergenceTolerance: 10.0));

        Assert.Equal(EstimationStatus.NotConverged, notConverged.Parameters!.Status);
        Assert.Equal(EstimationStatus.Converged, converged.Parameters!.Status);
    }

    [Fact]
    public void Enabling_string_similarity_flips_classification_for_notation_only_differences()
    {
        // Same repro as ISSUE-Eyu-20260905-fieldcomparator-literal-match-misses-duplicates: two
        // fields differing only by punctuation/hyphenation literally disagree on both, but agree
        // on both under string similarity. With the default 0.9/0.1 heuristic M/U probabilities
        // (batch below MinimumPairsForEmEstimation), 2 agreeing fields yield LLR = 2*ln(9) ~= 4.394
        // (Match, >= 4.0), while 2 disagreeing fields yield LLR = 2*ln(1/9) ~= -4.394
        // (NonMatch, <= -4.0).
        var records = new[]
        {
            Record("rec-1", ("name", "Nuclear Power Ler Co., Ltd."), ("registrationNumber", "110111-1234567")),
            Record("rec-2", ("name", "Nuclear Power Ler Co Ltd"), ("registrationNumber", "1101111234567")),
        };

        var literalAnalysis = LinkagePipeline.Analyze(records);
        var similarityAnalysis = LinkagePipeline.Analyze(records, new LinkageOptions(UseStringSimilarityComparator: true));

        Assert.Equal(LinkageClassification.NonMatch, Assert.Single(literalAnalysis.PairLinkages).Classification);
        Assert.Equal(LinkageClassification.Match, Assert.Single(similarityAnalysis.PairLinkages).Classification);
    }
}
