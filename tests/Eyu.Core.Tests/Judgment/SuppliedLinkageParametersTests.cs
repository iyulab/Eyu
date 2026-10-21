using Eyu.Core.Inference;
using Eyu.Core.Judgment;
using Eyu.Core.Linkage;
using Eyu.Core.Ports;
using Eyu.Core.Records;
using Eyu.Core.Tests.Linkage;
using Xunit;

namespace Eyu.Core.Tests.Judgment;

/// <summary>
/// A proposer given parameters fitted once on the population scores every call with them: a call of
/// one new record against the people already known is judged on the population's scale, not on a fit
/// over its own handful of pairs — the per-call fit is what reads a shared school or year as identity.
/// </summary>
public sealed class SuppliedLinkageParametersTests
{
    private const string Response = """{"entities": [], "relations": []}""";

    private static readonly KnownEntity Kim = new("key:kim", "김민지", "Person",
        [new RawRecord("p-1", new Dictionary<string, string?> { ["name"] = "김민지", ["birth"] = "1990-03-02", ["school"] = "한빛고" })]);

    private static readonly KnownEntity Lee = new("key:lee", "이수진", "Person",
        [new RawRecord("p-2", new Dictionary<string, string?> { ["name"] = "이수진", ["birth"] = "1991-07-15", ["school"] = "한빛고" })]);

    // How often two different people agree on each field — a school is shared by many, a birth date by few.
    private static readonly FieldLinkageParameters Population = FieldLinkageParameters.FromNonMatchAgreement(
        BinaryLevels.Of(new Dictionary<string, double> { ["name"] = 0.001, ["birth"] = 0.0005, ["school"] = 0.2 }),
        matchAgreement: 0.95,
        matchPrior: 0.01);

    private static RawRecord Newcomer(string id, string name, string birth) =>
        new(id, new Dictionary<string, string?> { ["name"] = name, ["birth"] = birth, ["school"] = "한빛고" });

    [Fact]
    public async Task Every_call_is_scored_with_the_supplied_parameters_and_fits_nothing()
    {
        var proposer = new SinglePassOntologyProposer(new StubModelClient(Response), linkageParameters: Population);

        var first = await proposer.ProposeAsync([], [Newcomer("n-1", "김민지", "1990-03-02")], PriorKnowledge.Of([Kim, Lee]), TestContext.Current.CancellationToken);
        var second = await proposer.ProposeAsync([], [Newcomer("n-2", "박서준", "1992-01-09")], PriorKnowledge.Of([Kim, Lee]), TestContext.Current.CancellationToken);

        Assert.Same(Population, first.Linkage!.Analysis.Parameters);
        Assert.Same(Population, second.Linkage!.Analysis.Parameters);
        Assert.Equal(EstimationStatus.Supplied, first.Linkage.Analysis.Parameters!.Status);

        var kim = Assert.Single(first.Linkage.Analysis.KnownCandidates);
        Assert.Equal(("n-1", "key:kim", LinkageClassification.Match), (kim.RecordId, kim.KnownEntityKey, kim.Classification));
        Assert.Empty(second.Linkage.Analysis.KnownCandidates);
    }

    /// <summary>
    /// A call of one record against two known people has two pairs — too few to fit, so it falls back to
    /// default parameters and puts a stranger who only shares their school to the model as possibly either
    /// of them. Scored with the population's parameters, where a school is shared by many, the stranger is
    /// no candidate.
    /// </summary>
    [Fact]
    public async Task A_stranger_sharing_only_a_common_value_is_no_candidate_on_the_populations_scale()
    {
        var supplied = new SinglePassOntologyProposer(new StubModelClient(Response), linkageParameters: Population);
        var perCall = new SinglePassOntologyProposer(new StubModelClient(Response));
        RawRecord[] stranger = [Newcomer("n-3", "정하늘", "1993-11-30")];

        var proposal = await supplied.ProposeAsync([], stranger, PriorKnowledge.Of([Kim, Lee]), TestContext.Current.CancellationToken);
        var fitted = await perCall.ProposeAsync([], stranger, PriorKnowledge.Of([Kim, Lee]), TestContext.Current.CancellationToken);

        Assert.Empty(proposal.Linkage!.Analysis.KnownCandidates);
        Assert.Equal(EstimationStatus.HeuristicDefault, fitted.Linkage!.Analysis.Parameters!.Status);
        Assert.Equal(["key:kim", "key:lee"], fitted.Linkage.Analysis.KnownCandidates
            .Where(c => c.Classification == LinkageClassification.GrayZone).Select(c => c.KnownEntityKey).Order());
    }

    [Fact]
    public async Task Without_supplied_parameters_each_call_fits_its_own()
    {
        var proposer = new SinglePassOntologyProposer(new StubModelClient(Response));

        var proposal = await proposer.ProposeAsync([], [Newcomer("n-1", "김민지", "1990-03-02")], PriorKnowledge.Of([Kim, Lee]), TestContext.Current.CancellationToken);

        Assert.NotEqual(EstimationStatus.Supplied, proposal.Linkage!.Analysis.Parameters!.Status);
    }
}
