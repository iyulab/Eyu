using Eyu.Core.Judgment;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Judgment;

/// <summary>
/// Known types: type names earlier work already uses, handed back so a new call names the same kind
/// of thing the same way. Whether a kind is the one a name stands for is the model's judgment; the
/// prompt asks for it, and the parse settles only the spelling.
/// </summary>
public sealed class KnownTypeProposalTests
{
    private static readonly RawRecord ErpBearing = new("erp-part-1", new Dictionary<string, string?> { ["part_no"] = "6205", ["name"] = "베어링 6205" });

    private static readonly KnownType Part = new("part", [ErpBearing]);
    private static readonly KnownType WorkOrder = new("work_order");

    private static readonly IReadOnlyList<RawRecord> Purchases =
    [
        new("po-1", new Dictionary<string, string?> { ["po_no"] = "PO-24-0913", ["item"] = "벨트 B-1200" }),
    ];

    private static string Response(string beltType, string poType = "purchase_order") => $$"""
        {
          "entities": [
            {"id": "e1", "name": "벨트 B-1200", "type": "{{beltType}}", "claim": "po-1 orders the belt", "sources": ["po-1"], "denotedBy": [], "confidence": 0.8},
            {"id": "e2", "name": "PO-24-0913", "type": "{{poType}}", "claim": "po-1 is a purchase order", "sources": ["po-1"], "denotedBy": ["po-1"], "confidence": 0.9}
          ],
          "relations": []
        }
        """;

    [Fact]
    public async Task Without_known_types_the_request_is_the_one_a_call_always_sent()
    {
        var plain = new StubModelClient(Response("item"));
        var withNone = new StubModelClient(Response("item"));

        await new SinglePassOntologyProposer(plain).ProposeAsync([], Purchases, TestContext.Current.CancellationToken);
        await new SinglePassOntologyProposer(withNone).ProposeAsync([], Purchases, PriorKnowledge.Of(Array.Empty<KnownType>()), TestContext.Current.CancellationToken);

        Assert.Equal(plain.LastPrompt, withNone.LastPrompt);
        Assert.DoesNotContain("Known type", plain.LastPrompt!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Known_types_are_listed_with_their_specimens_before_the_records_and_the_schema_is_unchanged()
    {
        var plain = new StubModelClient(Response("item"));
        var model = new StubModelClient(Response("item"));

        await new SinglePassOntologyProposer(plain).ProposeAsync([], Purchases, TestContext.Current.CancellationToken);
        await new SinglePassOntologyProposer(model).ProposeAsync([], Purchases, PriorKnowledge.Of([Part, WorkOrder]), TestContext.Current.CancellationToken);

        var prompt = model.LastPrompt!;
        Assert.Contains("Known type: part\n  - erp-part-1: part_no=6205, name=베어링 6205", prompt.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("Known type: work_order", prompt, StringComparison.Ordinal);
        Assert.Contains("rather than a new name for the same kind", prompt, StringComparison.Ordinal);
        Assert.Contains("Known types' records are shown for comparison only and cannot be cited as sources", prompt, StringComparison.Ordinal);
        Assert.True(prompt.IndexOf("Known type:", StringComparison.Ordinal) < prompt.IndexOf("Records:", StringComparison.Ordinal),
            "the known types come before the records whose kinds they name");
        Assert.Equal(plain.LastRequest!.ResponseSchema!.Value.GetRawText(), model.LastRequest!.ResponseSchema!.Value.GetRawText());
    }

    [Theory]
    [InlineData("Part")]
    [InlineData("PART")]
    [InlineData(" part ")]
    public async Task An_answered_type_that_spells_a_known_type_is_written_as_the_known_type(string answered)
    {
        var proposal = await new SinglePassOntologyProposer(new StubModelClient(Response(answered)))
            .ProposeAsync([], Purchases, PriorKnowledge.Of([Part]), TestContext.Current.CancellationToken);

        Assert.Equal("part", Assert.Single(proposal.Entities, e => e.EntityId == "e1").EntityType);
    }

    [Theory]
    [InlineData("WorkOrder")]
    [InlineData("work order")]
    [InlineData("Work-Order")]
    public async Task Separators_and_case_do_not_make_a_second_type(string answered)
    {
        var proposal = await new SinglePassOntologyProposer(new StubModelClient(Response("item", poType: answered)))
            .ProposeAsync([], Purchases, PriorKnowledge.Of([WorkOrder]), TestContext.Current.CancellationToken);

        Assert.Equal("work_order", Assert.Single(proposal.Entities, e => e.EntityId == "e2").EntityType);
    }

    [Fact]
    public async Task A_different_name_is_left_as_answered_since_meaning_is_not_settled_by_spelling()
    {
        // "item" for a part is the drift the prompt asks the model to avoid; the parse cannot tell that
        // an item is a part, so it does not pretend to.
        var proposal = await new SinglePassOntologyProposer(new StubModelClient(Response("item")))
            .ProposeAsync([], Purchases, PriorKnowledge.Of([Part]), TestContext.Current.CancellationToken);

        Assert.Equal("item", Assert.Single(proposal.Entities, e => e.EntityId == "e1").EntityType);
    }

    [Fact]
    public async Task A_known_entitys_type_is_a_name_in_use_too()
    {
        var supplier = new RawRecord("erp-v-1", new Dictionary<string, string?> { ["vendor"] = "KBS베어링" });
        var known = new KnownEntity("key:vendor-1", "KBS베어링", "Purchase_Order", [supplier]);

        var proposal = await new SinglePassOntologyProposer(new StubModelClient(Response("item", poType: "purchaseOrder")))
            .ProposeAsync([], Purchases, PriorKnowledge.Of([known]), TestContext.Current.CancellationToken);

        Assert.Equal("Purchase_Order", Assert.Single(proposal.Entities, e => e.EntityId == "e2").EntityType);
    }

    [Fact]
    public async Task Where_a_known_type_and_a_known_entitys_type_spell_one_name_the_known_type_is_the_spelling_kept()
    {
        var supplier = new RawRecord("erp-v-1", new Dictionary<string, string?> { ["vendor"] = "KBS베어링" });
        var known = new KnownEntity("key:vendor-1", "KBS베어링", "PurchaseOrder", [supplier]);

        var proposal = await new SinglePassOntologyProposer(new StubModelClient(Response("item", poType: "purchase order")))
            .ProposeAsync([], Purchases, new PriorKnowledge([known], [], [new KnownType("purchase_order")]), TestContext.Current.CancellationToken);

        Assert.Equal("purchase_order", Assert.Single(proposal.Entities, e => e.EntityId == "e2").EntityType);
    }

    public static TheoryData<string, KnownType[]> Refused => new()
    {
        { "a blank name", [new KnownType("  ")] },
        { "a name with no letter or digit", [new KnownType("__")] },
        { "one name written twice", [new KnownType("work_order"), new KnownType("WorkOrder")] },
        { "a specimen that is one of the call's own records", [new KnownType("purchase_order", [new RawRecord("po-1", new Dictionary<string, string?>())])] },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Known_types_that_cannot_mean_one_thing_are_refused_before_the_model_is_asked(string because, KnownType[] knownTypes)
    {
        var model = new StubModelClient(Response("item"));

        await Assert.ThrowsAsync<ArgumentException>(() => new SinglePassOntologyProposer(model)
            .ProposeAsync([], Purchases, PriorKnowledge.Of(knownTypes), TestContext.Current.CancellationToken));

        Assert.True(model.LastPrompt is null, because);
    }

    [Fact]
    public void The_known_types_fingerprint_is_its_own_and_leaves_the_others_where_they_were()
    {
        Assert.Matches("^[0-9a-f]{8}$", SinglePassOntologyProposer.KnownTypesPromptFingerprint);
        Assert.Equal("d911c6a8", SinglePassOntologyProposer.KnownEntitiesPromptFingerprint);
        Assert.NotEqual(SinglePassOntologyProposer.PromptFingerprint, SinglePassOntologyProposer.KnownTypesPromptFingerprint);
        Assert.NotEqual(SinglePassOntologyProposer.KnownEntitiesPromptFingerprint, SinglePassOntologyProposer.KnownTypesPromptFingerprint);
    }

    [Theory]
    [InlineData("work_order", "WorkOrder", true)]
    [InlineData("work_order", "work order", true)]
    [InlineData("설비", "설비", true)]
    [InlineData("part", "item", false)]
    [InlineData("part", "parts", false)]
    public void Type_names_are_the_same_when_only_case_and_separators_differ(string a, string b, bool same)
        => Assert.Equal(same, TypeNames.SameType(a, b));
}
