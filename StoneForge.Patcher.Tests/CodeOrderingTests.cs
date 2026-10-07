using StoneForge.MslHost;
using UndertaleModLib.Models;

public sealed class CodeOrderingTests
{
    [Fact]
    public void Newly_inserted_children_follow_their_own_parent_as_a_group()
    {
        var first = new UndertaleCode();
        var second = new UndertaleCode();
        var firstChild = new UndertaleCode { ParentEntry = first };
        var secondChild = new UndertaleCode { ParentEntry = second };
        var codes = new List<UndertaleCode> { secondChild, first, second, firstChild };
        CodeOrdering.Normalize(codes);
        Assert.Equal(new[] { first, firstChild, second, secondChild }, codes);
        CodeOrdering.Normalize(codes);
        Assert.Equal(new[] { first, firstChild, second, secondChild }, codes);
    }

    [Fact]
    public void Missing_parent_is_rejected_without_changing_the_list()
    {
        var child = new UndertaleCode { ParentEntry = new UndertaleCode() };
        var codes = new List<UndertaleCode> { child };
        Assert.Throws<InvalidDataException>(() => CodeOrdering.Normalize(codes));
        Assert.Same(child, Assert.Single(codes));
    }

    [Fact]
    public void Cyclic_parents_are_rejected()
    {
        var first = new UndertaleCode();
        var second = new UndertaleCode { ParentEntry = first };
        first.ParentEntry = second;
        Assert.Throws<InvalidDataException>(() => CodeOrdering.Normalize(new List<UndertaleCode> { first, second }));
    }
}
