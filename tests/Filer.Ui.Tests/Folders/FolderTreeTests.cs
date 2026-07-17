using Filer.ApiClient.Generated.Models;
using Filer.Ui.Folders;
using FluentAssertions;
using Xunit;

namespace Filer.Ui.Tests.Folders;

public sealed class FolderTreeTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid C = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid D = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static FolderListItemResponse Folder(Guid id, string name, Guid? parent = null) =>
        new() { Id = id, Name = name, ParentId = parent };

    [Fact]
    public void Assembles_nesting_and_orders_siblings_by_name()
    {
        var tree = FolderTree.Build(
        [
            Folder(B, "Zeta"),
            Folder(A, "Alpha"),
            Folder(C, "Child", parent: A),
            Folder(D, "Grandchild", parent: C),
        ]);

        tree.Should().HaveCount(2);
        tree[0].Name.Should().Be("Alpha");
        tree[1].Name.Should().Be("Zeta");
        tree[0].Children.Should().ContainSingle().Which.Name.Should().Be("Child");
        tree[0].Children[0].Children.Should().ContainSingle().Which.Name.Should().Be("Grandchild");
    }

    [Fact]
    public void A_folder_with_an_unknown_parent_surfaces_at_the_root()
    {
        var tree = FolderTree.Build([Folder(A, "Orphan", parent: Guid.NewGuid())]);

        tree.Should().ContainSingle().Which.Name.Should().Be("Orphan");
    }

    [Fact]
    public void Descendants_cover_the_whole_subtree_but_not_the_folder_itself()
    {
        FolderListItemResponse[] folders =
        [
            Folder(A, "Root"),
            Folder(B, "Child", parent: A),
            Folder(C, "Grandchild", parent: B),
            Folder(D, "Elsewhere"),
        ];

        IReadOnlySet<Guid> descendants = FolderTree.DescendantIds(folders, A);

        descendants.Should().BeEquivalentTo([B, C]);
    }
}
