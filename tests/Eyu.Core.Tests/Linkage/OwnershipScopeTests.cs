using Eyu.Core.Declared;
using Eyu.Core.Judgment;
using Eyu.Core.Linkage;
using Eyu.Core.Primitives;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

/// <summary>
/// Rows of a repeated section — the items checked on an inspection sheet — are owned by their sheet.
/// With the sheet number left out of what is compared, rows of different sheets agreed on their cells
/// (the same unit, the same verdict) and a seven-row batch was pre-linked across sheets as one entity,
/// with the model told not to re-decide — even a row of a different item. Two rows of different
/// sheets are two rows, so the pre-filter does not compare them; rows of one sheet still are.
/// </summary>
public class OwnershipScopeTests
{
    private static readonly SubjectRef Sheet = SubjectRef.Create("inspection_log");
    private static readonly SubjectRef Item = SubjectRef.Create("inspection_item");

    private static readonly DeclaredStructure[] Declared =
    [
        new(Sheet, [new DeclaredField("sheet_no")],
            [new DeclaredRelation("items", Item, ViaField: "sheet_no", Kind: DeclaredRelationKind.Child)]),
        new(Item, [new DeclaredField("sheet_no")],
            [new DeclaredRelation("sheet", Sheet, ViaField: "sheet_no", Kind: DeclaredRelationKind.Reference)]),
    ];

    private static RawRecord Row(string id, string sheet, string item, string reading, string unit, string judgment) =>
        new(id, new Dictionary<string, string?>
        {
            ["sheet_no"] = sheet,
            ["item"] = item,
            ["reading"] = reading,
            ["unit"] = unit,
            ["judgment"] = judgment,
        }, Item);

    /// <summary>The rows of three sheets, as a section adapter hands them over.</summary>
    private static readonly RawRecord[] Rows =
    [
        Row("IL-1001/0", "IL-1001", "hydraulic pressure", "142", "bar", "pass"),
        Row("IL-1001/1", "IL-1001", "oil temperature", "48", "C", "pass"),
        Row("IL-1001/2", "IL-1001", "noise", "82", "dB", "fail"),
        Row("IL-1002/0", "IL-1002", "hydraulic pressure", "128", "bar", "fail"),
        Row("IL-1002/1", "IL-1002", "oil temperature", "51", "C", "pass"),
        Row("IL-1003/0", "IL-1003", "discharge pressure", "7.2", "bar", "pass"),
        Row("IL-1003/1", "IL-1003", "drain", "ok", "", "pass"),
    ];

    private static string SheetOf(string rowId) => rowId[..rowId.IndexOf('/', StringComparison.Ordinal)];

    [Fact]
    public void Rows_of_one_sheet_share_a_scope_and_rows_of_two_sheets_do_not()
    {
        var scope = LinkageEvidence.OwnershipScope(Rows, Declared);

        Assert.Equal(scope(Rows[0]), scope(Rows[1]));
        Assert.NotEqual(scope(Rows[0]), scope(Rows[3]));
    }

    [Fact]
    public void A_parent_key_compares_as_a_blocking_key_does()
    {
        var scope = LinkageEvidence.OwnershipScope(Rows, Declared);

        Assert.Equal(scope(Row("a", "il-1001 ", "x", "1", "u", "pass")), scope(Rows[0]));
    }

    [Fact]
    public void A_record_owned_by_nothing_has_no_scope()
    {
        var scope = LinkageEvidence.OwnershipScope(Rows, Declared);
        var sheet = new RawRecord("IL-1001", new Dictionary<string, string?> { ["sheet_no"] = "IL-1001" }, Sheet);
        var noSubject = Rows[0] with { Id = "loose", Subject = null };
        var noKey = Row("x", " ", "noise", "80", "dB", "pass");

        Assert.Null(scope(sheet));
        Assert.Null(scope(noSubject));
        Assert.Null(scope(noKey));
    }

    [Fact]
    public void A_section_read_without_its_sheet_is_scoped_by_its_declared_parent()
    {
        DeclaredStructure[] sectionAlone =
        [
            new(Item, [new DeclaredField("sheet_no")],
                [new DeclaredRelation("sheet", Sheet, ViaField: "sheet_no", Kind: DeclaredRelationKind.Parent)]),
        ];
        var scope = LinkageEvidence.OwnershipScope(Rows, sectionAlone);

        Assert.Equal(scope(Rows[0]), scope(Rows[1]));
        Assert.NotEqual(scope(Rows[0]), scope(Rows[3]));
        Assert.Contains("sheet_no", LinkageEvidence.ReferenceFieldsBySubject(sectionAlone)[Item]);
    }

    [Fact]
    public void A_reference_alone_owns_nothing()
    {
        DeclaredStructure[] referenceOnly = [Declared[1]];

        Assert.Null(LinkageEvidence.OwnershipScope(Rows, referenceOnly)(Rows[0]));
    }

    [Fact]
    public void The_pipeline_compares_rows_only_within_their_sheet()
    {
        var view = LinkageEvidence.IdentityView(Rows, Declared);

        var unscoped = LinkagePipeline.Analyze(view, [], comparisonScope: null);
        var scoped = LinkagePipeline.Analyze(view, [], comparisonScope: LinkageEvidence.OwnershipScope(Rows, Declared));

        // The positive control: unscoped, pairs cross sheets.
        Assert.Contains(unscoped.PairLinkages, p => SheetOf(p.RecordIdA) != SheetOf(p.RecordIdB));
        Assert.NotEmpty(scoped.PairLinkages);
        Assert.All(scoped.PairLinkages, p => Assert.Equal(SheetOf(p.RecordIdA), SheetOf(p.RecordIdB)));
        Assert.All(scoped.Clustering.Clusters, c => Assert.Single(c.RecordIds.Select(SheetOf).Distinct()));
    }

    [Fact]
    public void A_row_is_not_compared_with_a_known_row_of_another_sheet()
    {
        var known = new KnownEntity("item-1002-0", "hydraulic pressure", "inspection_item", [Rows[3]]);
        var view = LinkageEvidence.IdentityView([Rows[0], Rows[5]], Declared);

        var analysis = LinkagePipeline.Analyze(view, [known], comparisonScope: LinkageEvidence.OwnershipScope([Rows[0], Rows[5]], Declared));

        Assert.Empty(analysis.KnownCandidates);
    }

    [Fact]
    public async Task The_proposer_does_not_tell_the_model_rows_of_different_sheets_are_one_entity()
    {
        var model = new StubModelClient("""{"entities":[],"relations":[]}""");

        var proposal = await new SinglePassOntologyProposer(model).ProposeAsync(Declared, Rows, TestContext.Current.CancellationToken);

        Assert.All(proposal.Linkage!.Analysis.Clustering.Clusters, c => Assert.Single(c.RecordIds.Select(SheetOf).Distinct()));
        Assert.DoesNotContain("IL-1001/0, IL-1002/0", model.LastPrompt);
    }
}
