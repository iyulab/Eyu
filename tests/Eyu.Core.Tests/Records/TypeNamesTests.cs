using System.Text;
using Eyu.Core.Records;
using Xunit;

namespace Eyu.Core.Tests.Records;

/// <summary>Two spellings of a type name are one name whatever Unicode form they arrive in: text saved on macOS arrives decomposed, and what renders the same compares the same.</summary>
public class TypeNamesTests
{
    [Theory]
    [InlineData("작업지시", "작업지시")]
    [InlineData("작업 지시", "작업지시")]
    [InlineData("Café", "Café")]
    [InlineData("WorkOrder", "ＷｏｒｋＯｒｄｅｒ")]
    [InlineData("work_order", "Work Order")]
    public void Spellings_of_one_name_are_the_same_type_in_every_unicode_form(string a, string b)
    {
        Assert.True(TypeNames.SameType(a, b));
        Assert.True(TypeNames.SameType(a.Normalize(NormalizationForm.FormD), b));
        Assert.True(TypeNames.SameType(a, b.Normalize(NormalizationForm.FormD)));
        Assert.True(TypeNames.SameType(a.Normalize(NormalizationForm.FormD), b.Normalize(NormalizationForm.FormC)));
    }

    [Theory]
    [InlineData("part", "item")]
    [InlineData("작업지시", "작업지사")]
    [InlineData("Café", "Cafe")]
    public void Different_names_stay_different(string a, string b)
    {
        Assert.False(TypeNames.SameType(a, b));
        Assert.False(TypeNames.SameType(a.Normalize(NormalizationForm.FormD), b));
    }
}
