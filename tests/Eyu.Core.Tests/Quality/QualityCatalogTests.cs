using Xunit;

namespace Eyu.Core.Tests.Quality;

public class QualityCatalogTests
{
    // The live measurement keys its per-case statistics by name across both regimes, so a document
    // case sharing a record case's name would silently merge two cases' numbers into one row.
    [Fact]
    public void Case_names_are_unique_across_both_regimes()
    {
        var names = QualityCatalog.Cases.Select(c => c.Name).Concat(QualityCatalog.DocumentCases.Select(c => c.Name)).ToList();

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
}
