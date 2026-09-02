using Eyu.Core.Declared;
using Eyu.Core.Inference;
using Eyu.Core.Ports;
using Eyu.Core.Primitives;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Ports;

/// <summary>
/// Proves each of the four consumer-implemented ports (IStructureSource / IRecordSample /
/// IModelClient — plus IGroundingContract, covered by <c>GroundedClaimTests</c>) can be satisfied
/// end to end by a minimal stub. Same role as <c>SchemaProposerCouplingSmokeTests</c> in the
/// integration-smoke project: proves the type surface resolves and runs, not a design commitment
/// about a real implementation. IOntologyProposer is intentionally not covered here — its shape is
/// entangled with the still-open internal-organization question (see ROADMAP.md).
/// </summary>
public class PortContractSmokeTests
{
    [Fact]
    public async Task IStructureSource_stub_returns_declared_structure_for_a_known_subject()
    {
        IStructureSource source = new StubStructureSource();
        var subject = SubjectRef.Create("invoice");

        var structure = await source.GetStructureAsync(subject);

        Assert.NotNull(structure);
        Assert.Equal(subject, structure!.Subject);
        Assert.Single(structure.Fields);
    }

    [Fact]
    public async Task IStructureSource_stub_returns_null_for_an_undeclared_subject()
    {
        IStructureSource source = new StubStructureSource();

        var structure = await source.GetStructureAsync(SubjectRef.Create("unknown"));

        Assert.Null(structure);
    }

    [Fact]
    public async Task IRecordSample_stub_returns_up_to_the_requested_count()
    {
        IRecordSample sample = new StubRecordSample();

        var records = await sample.SampleAsync(SubjectRef.Create("invoice"), maxCount: 1);

        Assert.Single(records);
    }

    [Fact]
    public async Task IModelClient_stub_completes_a_request()
    {
        IModelClient model = new StubModelClient();

        var response = await model.CompleteAsync(new ModelRequest("classify: invoice #42"));

        Assert.Equal("stub-response", response.Text);
    }

    private sealed class StubStructureSource : IStructureSource
    {
        public Task<DeclaredStructure?> GetStructureAsync(SubjectRef subject, CancellationToken cancellationToken = default)
        {
            if (subject.Value != "invoice")
            {
                return Task.FromResult<DeclaredStructure?>(null);
            }

            var structure = new DeclaredStructure(
                subject,
                Fields: [new DeclaredField("total")],
                Relations: []);

            return Task.FromResult<DeclaredStructure?>(structure);
        }
    }

    private sealed class StubRecordSample : IRecordSample
    {
        public Task<IReadOnlyList<RawRecord>> SampleAsync(SubjectRef subject, int maxCount, CancellationToken cancellationToken = default)
        {
            var records = new List<RawRecord>
            {
                new("rec-1", new Dictionary<string, string?> { ["total"] = "100" }),
                new("rec-2", new Dictionary<string, string?> { ["total"] = "200" }),
            };

            return Task.FromResult<IReadOnlyList<RawRecord>>(records.Take(maxCount).ToList());
        }
    }

    private sealed class StubModelClient : IModelClient
    {
        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelResponse("stub-response"));
    }
}
