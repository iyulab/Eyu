using Eyu.Core.Proposals;
using Xunit;

namespace Eyu.Core.Tests.Proposals;

public class InnateVocabularyTests
{
    [Theory]
    [InlineData("Person")]
    [InlineData("organization")]
    [InlineData("EVENT")]
    [InlineData("Action")]
    [InlineData("location")]
    [InlineData("Time")]
    public void Every_innate_entity_type_is_Innate_in_any_case(string type)
    {
        Assert.Equal(VocabularyOrigin.Innate, InnateVocabulary.OfEntityType(type));
    }

    [Theory]
    [InlineData("PartOf")]
    [InlineData("part_of")]
    [InlineData("participates-in")]
    [InlineData("LOCATED_IN")]
    [InlineData("occursAt")]
    public void Every_innate_relation_name_is_Innate_across_naming_conventions(string name)
    {
        Assert.Equal(VocabularyOrigin.Innate, InnateVocabulary.OfRelationName(name));
    }

    [Theory]
    [InlineData("Company")]
    [InlineData("People")]
    [InlineData("Organisation")]
    [InlineData("WorkOrder")]
    public void A_domain_word_or_a_near_miss_is_Acquired_because_there_is_no_synonym_table(string type)
    {
        // Folding Company into Organization would be term normalization (design rationale §B);
        // the tag reports the vocabulary the model actually used.
        Assert.Equal(VocabularyOrigin.Acquired, InnateVocabulary.OfEntityType(type));
    }

    [Fact]
    public void The_design_rationale_lists_exactly_the_vocabulary_the_code_uses()
    {
        // The list is an identifier list in prose, so it is checked against the code rather than
        // trusted to stay in step by hand.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Eyu.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var rationale = string.Join(' ', File.ReadAllText(Path.Combine(root.FullName, "docs", "philosophy.md"))
            .Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains(
            $"entity types {string.Join(", ", InnateVocabulary.EntityTypes)}, and relation names {string.Join(", ", InnateVocabulary.RelationNames)} ",
            rationale);
    }

    [Fact]
    public void An_entity_type_is_not_innate_just_because_it_is_an_innate_relation_name()
    {
        Assert.Equal(VocabularyOrigin.Acquired, InnateVocabulary.OfEntityType("PartOf"));
        Assert.Equal(VocabularyOrigin.Acquired, InnateVocabulary.OfRelationName("Person"));
    }
    [Fact]
    public void An_innate_type_written_in_full_width_letters_is_Innate()
    {
        Assert.Equal(VocabularyOrigin.Innate, InnateVocabulary.OfEntityType("Ｐｅｒｓｏｎ"));
    }
}
