using Eyu.Core.Primitives;
using Xunit;

namespace Eyu.Core.Tests.Primitives;

public class SubjectRefTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_input(string? value)
    {
        Assert.Throws<ArgumentException>(() => SubjectRef.Create(value!));
    }

    [Fact]
    public void Create_trims_the_value()
    {
        var subject = SubjectRef.Create("  invoice  ");

        Assert.Equal("invoice", subject.Value);
    }

    [Fact]
    public void ToString_returns_the_value()
    {
        var subject = SubjectRef.Create("invoice");

        Assert.Equal("invoice", subject.ToString());
    }
}
