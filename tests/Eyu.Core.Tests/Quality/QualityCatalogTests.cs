using Eyu.Core.Linkage;
using Eyu.Core.Primitives;
using Xunit;

namespace Eyu.Core.Tests.Quality;

public class QualityCatalogTests
{
    // The live measurement keys its per-case statistics by name across every regime, so a document
    // case sharing a record case's name would silently merge two cases' numbers into one row.
    [Fact]
    public void Case_names_are_unique_across_every_regime()
    {
        var names = QualityCatalog.Cases.Select(c => c.Name)
            .Concat(QualityCatalog.DocumentCases.Select(c => c.Name))
            .Concat(QualityCatalog.CrossSourceCases.Select(c => c.Name))
            .ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    // The live measurement is excluded from a default test run, so this is where a malformed
    // document case — a question the construction rules refuse, a repeated chunk id the pre-filter
    // would refuse — fails first rather than minutes into a run against a real model.
    [Fact]
    public void Each_document_case_has_chunks_under_distinct_ids_and_questions_to_reach()
    {
        Assert.NotEmpty(QualityCatalog.DocumentCases);
        foreach (var documentCase in QualityCatalog.DocumentCases)
        {
            Assert.NotEmpty(documentCase.Chunks);
            Assert.Equal(documentCase.Chunks.Length, documentCase.Chunks.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.All(documentCase.Chunks, chunk => Assert.False(string.IsNullOrWhiteSpace(Assert.Single(chunk.Fields).Value)));
            Assert.NotEmpty(documentCase.Questions);
        }
    }

    // A thing's names are the join the measurement counts individuals by, so a name that no record
    // carries would count a thing as missing that the model had no way to name, and a denoting record
    // that is not in any source would never be found.
    [Fact]
    public void Each_cross_source_thing_is_named_in_the_records_and_denoted_by_one_of_them()
    {
        Assert.NotEmpty(QualityCatalog.CrossSourceCases);
        foreach (var crossSource in QualityCatalog.CrossSourceCases)
        {
            Assert.True(crossSource.Sources.Length >= 2, $"{crossSource.Name} needs at least two sources to be cross-source");
            var records = crossSource.Sources.SelectMany(s => s.Records).ToList();
            Assert.Equal(records.Count, records.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count());
            var values = records.SelectMany(r => r.Fields.Values).OfType<string>().ToHashSet(StringComparer.Ordinal);
            foreach (var thing in crossSource.Things)
            {
                Assert.All(thing.Names, name => Assert.Contains(name, values));
                Assert.True(crossSource.Sources.Count(s => s.Records.Any(r => thing.Names.Any(n => r.Fields.Values.Contains(n)))) >= 2,
                    $"{thing.Label} is named by one source only");
                if (thing.DenotingRecord is { } id)
                {
                    Assert.Contains(records, r => r.Id == id);
                }
            }
        }
    }

    /// <summary>
    /// The section case's rows hold their sheet's number, which is the sheet's identity and not theirs:
    /// declared as the row's parent, it is left out of what the pre-filter compares, so two
    /// rows of one sheet do not look like one thing for sharing it.
    /// </summary>
    [Fact]
    public void The_section_case_leaves_the_sheet_number_out_of_what_identifies_a_row()
    {
        var sections = QualityCatalog.CrossSourceCases.Single(c => c.Name == "plant-inspection-log-sections");
        var declared = sections.Sources.SelectMany(s => s.Declarations).ToList();
        var rows = sections.Sources.Single(s => s.Name == "inspection-items").Records;

        var view = LinkageEvidence.IdentityView(rows, declared);

        Assert.All(rows, row => Assert.Equal(SubjectRef.Create("inspection_item"), row.Subject));
        Assert.All(view, row => Assert.DoesNotContain("sheet_no", row.Fields.Keys));
        Assert.All(view, row => Assert.Contains("item", row.Fields.Keys));
    }

    /// <summary>
    /// Read alone — the separate calls of the measurement, a connector per source — the rows' own
    /// declaration still says the sheet owns them, so rows of two sheets are not compared.
    /// </summary>
    [Fact]
    public void The_section_rows_alone_are_scoped_by_their_sheet()
    {
        var items = QualityCatalog.CrossSourceCases.Single(c => c.Name == "plant-inspection-log-sections")
            .Sources.Single(s => s.Name == "inspection-items");

        var scope = LinkageEvidence.OwnershipScope(items.Records, items.Declarations);

        Assert.All(items.Records, row => Assert.NotNull(scope(row)));
        Assert.Equal(3, items.Records.Select(scope).Distinct().Count());
    }
}
