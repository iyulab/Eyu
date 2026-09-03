using Eyu.Core.Linkage;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Linkage;

public class FieldComparatorTests
{
    private static RawRecord Record(string id, params (string Key, string? Value)[] fields) =>
        new(id, fields.ToDictionary(f => f.Key, f => f.Value));

    [Fact]
    public void A_field_with_the_same_normalized_value_on_both_sides_agrees()
    {
        var a = Record("rec-1", ("name", "Acme Corp"));
        var b = Record("rec-2", ("name", "  acme corp  "));

        var result = FieldComparator.Compare(a, b);

        Assert.Equal(FieldAgreementLevel.Agree, result["name"]);
    }

    [Fact]
    public void A_field_with_different_normalized_values_disagrees()
    {
        var a = Record("rec-1", ("city", "Springfield"));
        var b = Record("rec-2", ("city", "Portland"));

        var result = FieldComparator.Compare(a, b);

        Assert.Equal(FieldAgreementLevel.Disagree, result["city"]);
    }

    [Fact]
    public void A_field_missing_or_blank_on_either_side_is_excluded_not_disagreement()
    {
        var a = Record("rec-1", ("name", "Acme Corp"), ("city", null));
        var b = Record("rec-2", ("name", "Acme Corp"));

        var result = FieldComparator.Compare(a, b);

        Assert.False(result.ContainsKey("city"));
        Assert.Equal(FieldAgreementLevel.Agree, result["name"]);
    }

    [Fact]
    public void Only_fields_present_on_both_records_are_compared()
    {
        var a = Record("rec-1", ("name", "Acme Corp"), ("phone", "555-0100"));
        var b = Record("rec-2", ("name", "Acme Corp"), ("fax", "555-0199"));

        var result = FieldComparator.Compare(a, b);

        Assert.Single(result);
        Assert.True(result.ContainsKey("name"));
    }
}
