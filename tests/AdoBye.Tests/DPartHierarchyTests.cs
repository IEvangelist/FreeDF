using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace AdoBye.Tests;

public class DPartHierarchyTests
{
    static PdfDocument Load(string catalog = "/DPartRoot 4 0 R",
        string root = "/DPartRootNode 5 0 R", string node = "", string objects = "")
    {
        var pdf = "%PDF-2.0\n" +
            $"1 0 obj\n<< /Type /Catalog /Pages 2 0 R {catalog} /Vendor (catalog) >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>\nendobj\n" +
            $"4 0 obj\n<< {root} /Vendor (root) >>\nendobj\n" +
            $"5 0 obj\n<< {node} /Vendor (node) >>\nendobj\n" +
            objects + "trailer\n<< /Root 1 0 R /Size 20000 >>\n%%EOF\n";
        return PdfDocument.Load(Encoding.ASCII.GetBytes(pdf));
    }

    static PdfDPartRoot Read(PdfDocument doc)
    {
        var catalog = doc.File.Catalog;
        var before = catalog.Entries.ToArray();
        PdfDPartRoot? result = null;
        Assert.Null(Record.Exception(() => { result = doc.GetDPartRoot(); }));
        Assert.Equal(before, catalog.Entries.ToArray());
        var root = Assert.IsType<PdfDPartRoot>(result);
        Assert.Same(catalog.Get("DPartRoot"), root.RawValue);
        return root;
    }

    static PdfDPartNode ReadNode(PdfDocument doc)
    {
        var root = Read(doc);
        Assert.Equal(PdfDPartBoundary.None, root.Boundary);
        return Assert.IsType<PdfDPartNode>(root.RootNode);
    }

    [Fact]
    public void Missing_catalog_entry_has_no_hierarchy()
    {
        var doc = Load(catalog: "");
        Assert.Null(Record.Exception(() => Assert.Null(doc.GetDPartRoot())));
        Assert.Equal("catalog", Assert.IsType<PdfString>(doc.File.Catalog.Get("Vendor")).Text);
    }

    [Theory]
    [InlineData("/DPartRoot << /DPartRootNode 5 0 R >>", PdfDPartBoundary.InvalidReference)]
    [InlineData("/DPartRoot null", PdfDPartBoundary.InvalidReference)]
    [InlineData("/DPartRoot 999 0 R", PdfDPartBoundary.InvalidDictionary)]
    [InlineData("/DPartRoot 6 0 R", PdfDPartBoundary.InvalidDictionary)]
    public void Invalid_catalog_root_is_observable_and_preserves_raw_catalog(string catalog, PdfDPartBoundary boundary)
    {
        var root = Read(Load(catalog: catalog, objects: "6 0 obj\n42\nendobj\n"));
        Assert.Equal(boundary, root.Boundary);
        Assert.Null(root.RootNode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/DPartRootNode << /Type /DPart >>")]
    [InlineData("/DPartRootNode null")]
    [InlineData("/DPartRootNode 42")]
    public void Missing_or_direct_root_node_is_not_expanded(string entries)
    {
        var doc = Load(root: entries);
        var root = Read(doc);
        var node = Assert.IsType<PdfDPartNode>(root.RootNode);
        Assert.Equal(PdfDPartBoundary.InvalidReference, node.Boundary);
        Assert.Empty(node.Children);
        Assert.Same(doc.File.GetObject(4), root.Dictionary);
        Assert.Equal("root", Assert.IsType<PdfString>(root.Dictionary!.Get("Vendor")).Text);
        if (entries.Length > 0) Assert.Same(root.Dictionary.Get("DPartRootNode"), node.RawValue);
        else Assert.Null(node.RawValue);
    }

    [Theory]
    [InlineData("/DPartRootNode 999 0 R")]
    [InlineData("/DPartRootNode 6 0 R")]
    public void Unresolvable_or_non_dictionary_root_node_retains_its_indirect_reference(string entries)
    {
        var node = ReadNode(Load(root: entries, objects: "6 0 obj\n6 0 R\nendobj\n"));
        Assert.Equal(PdfDPartBoundary.InvalidDictionary, node.Boundary);
        Assert.IsType<PdfRef>(node.RawValue);
        Assert.Null(node.Dictionary);
        Assert.Empty(node.Children);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("/Type /DPartRoot", "/Type /DPart")]
    public void Optional_types_may_be_absent_or_correct(string rootType, string nodeType)
    {
        var doc = Load(root: $"{rootType} /DPartRootNode 5 0 R /RecordLevel 2 /NodeNameList [(record) /Vendor]",
            node: nodeType);
        var root = Read(doc);
        Assert.Equal(PdfDPartBoundary.None, root.Boundary);
        Assert.Equal(2L, Assert.IsType<PdfInteger>(root.RecordLevel).Value);
        Assert.Same(root.Dictionary!.Get("NodeNameList"), root.NodeNameList);
        Assert.Equal(PdfDPartBoundary.None, Assert.IsType<PdfDPartNode>(root.RootNode).Boundary);
        Assert.Same(doc.File.GetObject(5), root.RootNode!.Dictionary);
        Assert.Empty(root.RootNode.Children);
    }

    [Theory]
    [InlineData("/Type /Page")]
    [InlineData("/Type null")]
    [InlineData("/Type (DPartRoot)")]
    [InlineData("/Type 42")]
    public void Explicit_wrong_root_type_is_malformed_but_preserved(string type)
    {
        var root = Read(Load(root: $"{type} /DPartRootNode 5 0 R"));
        Assert.Equal(PdfDPartBoundary.InvalidType, root.Boundary);
        Assert.NotNull(root.Dictionary!.Get("Type"));
        Assert.Null(root.RootNode);
    }

    [Theory]
    [InlineData("/Type /Page")]
    [InlineData("/Type null")]
    [InlineData("/Type (DPart)")]
    [InlineData("/Type 42")]
    public void Explicit_wrong_node_type_is_malformed_but_preserved(string type)
    {
        var node = ReadNode(Load(node: type));
        Assert.Equal(PdfDPartBoundary.InvalidType, node.Boundary);
        Assert.NotNull(node.Dictionary!.Get("Type"));
        Assert.Empty(node.Children);
    }

    [Fact]
    public void Nested_children_preserve_grouping_order_raw_references_and_dictionaries()
    {
        var doc = Load(node: "/DParts [[6 0 R 7 0 R] [] [8 0 R]]", objects:
            "6 0 obj\n<< /Parent 5 0 R /DParts [[9 0 R]] >>\nendobj\n" +
            "7 0 obj\n<< >>\nendobj\n8 0 obj\n<< >>\nendobj\n9 0 obj\n<< >>\nendobj\n");
        var originalRoot = Assert.IsType<PdfDictionary>(doc.File.GetObject(4));
        var originalNode = Assert.IsType<PdfDictionary>(doc.File.GetObject(5));
        var rootEntries = originalRoot.Entries.ToArray();
        var nodeEntries = originalNode.Entries.ToArray();
        var node = ReadNode(doc);
        Assert.Equal(3, node.Children.Count);
        Assert.Equal(new[] { 6, 7 }, node.Children[0].Nodes.Select(n => n.Reference!.Number));
        Assert.Empty(node.Children[1].Nodes);
        Assert.Equal(8, Assert.Single(node.Children[2].Nodes).Reference!.Number);
        var first = node.Children[0].Nodes[0];
        Assert.Equal(9, Assert.Single(Assert.Single(first.Children).Nodes).Reference!.Number);
        Assert.Equal(new PdfRef(5, 0), first.Parent);
        Assert.Same(doc.File.GetObject(6), first.Dictionary);
        Assert.Same(Assert.IsType<PdfArray>(node.Dictionary!.Get("DParts"))[0], node.Children[0].RawValue);
        Assert.All(node.Children, g => Assert.Equal(PdfDPartBoundary.None, g.Boundary));
        Assert.Equal(PdfDPartBoundary.None, node.ChildrenBoundary);
        Assert.Equal(rootEntries, originalRoot.Entries.ToArray());
        Assert.Equal(nodeEntries, originalNode.Entries.ToArray());
        Assert.True(Assert.IsAssignableFrom<ICollection<PdfDPartGroup>>(node.Children).IsReadOnly);
        Assert.True(Assert.IsAssignableFrom<ICollection<PdfDPartNode>>(node.Children[0].Nodes).IsReadOnly);
    }

    [Fact]
    public void Local_indirect_arrays_and_optional_root_metadata_are_resolved_without_losing_raw_refs()
    {
        var doc = Load(root: "/Type 11 0 R /DPartRootNode 5 0 R /RecordLevel 12 0 R /NodeNameList 13 0 R",
            node: "/Type 14 0 R /DParts 6 0 R", objects:
            "6 0 obj\n[7 0 R]\nendobj\n7 0 obj\n[8 0 R]\nendobj\n8 0 obj\n<< >>\nendobj\n" +
            "11 0 obj\n/DPartRoot\nendobj\n12 0 obj\n2\nendobj\n" +
            "13 0 obj\n[/Record /Recipient]\nendobj\n14 0 obj\n/DPart\nendobj\n");
        var root = Read(doc);
        Assert.Equal(PdfDPartBoundary.None, root.Boundary);
        Assert.Equal(2L, Assert.IsType<PdfInteger>(root.RecordLevel).Value);
        Assert.Same(doc.File.GetObject(13), root.NodeNameList);
        Assert.IsType<PdfRef>(root.Dictionary!.Get("RecordLevel"));
        Assert.IsType<PdfRef>(root.Dictionary.Get("NodeNameList"));
        var node = Assert.IsType<PdfDPartNode>(root.RootNode);
        var group = Assert.Single(node.Children);
        Assert.Equal(new PdfRef(7, 0), group.RawValue);
        Assert.Equal(8, Assert.Single(group.Nodes).Reference!.Number);
        Assert.IsType<PdfRef>(node.Dictionary!.Get("DParts"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/DParts []")]
    [InlineData("/DParts [[]]")]
    [InlineData("/DParts [[6 0 R]]")]
    public void Missing_empty_and_single_child_groups_are_observable(string entries)
    {
        var node = ReadNode(Load(node: entries, objects: "6 0 obj\n<< >>\nendobj\n"));
        Assert.Equal(PdfDPartBoundary.None, node.Boundary);
        Assert.Equal(PdfDPartBoundary.None, node.ChildrenBoundary);
        if (entries == "/DParts [[6 0 R]]")
            Assert.Equal(6, Assert.Single(Assert.Single(node.Children).Nodes).Reference!.Number);
    }

    [Theory]
    [InlineData("/DParts null")]
    [InlineData("/DParts 42")]
    [InlineData("/DParts << >>")]
    public void Wrong_outer_array_is_reported_without_discarding_it(string entries)
    {
        var node = ReadNode(Load(node: entries));
        Assert.Equal(PdfDPartBoundary.InvalidChildren, node.ChildrenBoundary);
        Assert.NotNull(node.Dictionary!.Get("DParts"));
        Assert.Empty(node.Children);
    }

    [Fact]
    public void Malformed_groups_and_child_slots_do_not_hide_later_valid_children()
    {
        var node = ReadNode(Load(node: "/DParts [6 0 R null [<< >> 42 999 0 R 8 0 R 7 0 R]]", objects:
            "6 0 obj\n<< >>\nendobj\n7 0 obj\n<< >>\nendobj\n8 0 obj\n8 0 R\nendobj\n"));
        Assert.Equal(3, node.Children.Count);
        Assert.Equal(PdfDPartBoundary.InvalidChildren, node.Children[0].Boundary);
        Assert.Equal(PdfDPartBoundary.InvalidChildren, node.Children[1].Boundary);
        Assert.Equal(new[] {
            PdfDPartBoundary.InvalidReference, PdfDPartBoundary.InvalidReference,
            PdfDPartBoundary.InvalidDictionary, PdfDPartBoundary.InvalidDictionary, PdfDPartBoundary.None,
        }, node.Children[2].Nodes.Select(n => n.Boundary));
        Assert.IsType<PdfDictionary>(node.Children[2].Nodes[0].RawValue);
        Assert.Equal(7, node.Children[2].Nodes[4].Reference!.Number);
    }

    [Fact]
    public void Cycles_and_shared_or_repeated_children_have_distinct_boundaries_in_depth_first_order()
    {
        var node = ReadNode(Load(node: "/DParts [[6 0 R 7 0 R] [6 0 R]]", objects:
            "6 0 obj\n<< /DParts [[5 0 R 8 0 R]] >>\nendobj\n" +
            "7 0 obj\n<< /DParts [[8 0 R]] >>\nendobj\n8 0 obj\n<< >>\nendobj\n"));
        var six = node.Children[0].Nodes[0];
        var seven = node.Children[0].Nodes[1];
        Assert.Equal(PdfDPartBoundary.Cycle, six.Children[0].Nodes[0].Boundary);
        Assert.Equal(PdfDPartBoundary.None, six.Children[0].Nodes[1].Boundary);
        Assert.Equal(PdfDPartBoundary.RepeatedReference, seven.Children[0].Nodes[0].Boundary);
        var repeat = Assert.Single(node.Children[1].Nodes);
        Assert.Equal(PdfDPartBoundary.RepeatedReference, repeat.Boundary);
        Assert.Same(six.Dictionary, repeat.Dictionary);
        Assert.Same(node.Dictionary, six.Children[0].Nodes[0].Dictionary);
        Assert.Empty(repeat.Children);
    }

    [Fact]
    public void Depth_cap_stops_a_long_chain_and_preserves_the_boundary_dictionary()
    {
        var objects = new StringBuilder();
        for (var i = 6; i <= 5 + PdfDPartRoot.MaxDepth + 2; i++)
            objects.Append($"{i} 0 obj\n<< /DParts [[{i + 1} 0 R]] >>\nendobj\n");
        var node = ReadNode(Load(node: "/DParts [[6 0 R]]", objects: objects.ToString()));
        for (var depth = 0; depth < PdfDPartRoot.MaxDepth; depth++)
        {
            Assert.Equal(PdfDPartBoundary.None, node.Boundary);
            node = Assert.Single(Assert.Single(node.Children).Nodes);
        }
        Assert.Equal(PdfDPartBoundary.DepthLimit, node.Boundary);
        Assert.NotNull(node.Dictionary!.Get("DParts"));
        Assert.Empty(node.Children);
    }

    [Fact]
    public void Node_cap_counts_repeated_visits_and_preserves_unvisited_tail_in_raw_array()
    {
        var children = string.Join(" ", Enumerable.Repeat("6 0 R", PdfDPartRoot.MaxNodeVisits + 10));
        var node = ReadNode(Load(node: $"/DParts [[{children}] [7 0 R]]", objects: "6 0 obj\n<< >>\nendobj\n"));
        var group = Assert.Single(node.Children);
        Assert.Equal(PdfDPartRoot.MaxNodeVisits - 1, group.Nodes.Count);
        Assert.Equal(PdfDPartBoundary.None, group.Nodes[0].Boundary);
        Assert.All(group.Nodes.Skip(1), n => Assert.Equal(PdfDPartBoundary.RepeatedReference, n.Boundary));
        Assert.Equal(PdfDPartBoundary.NodeLimit, group.Boundary);
        Assert.Equal(PdfDPartBoundary.NodeLimit, node.ChildrenBoundary);
        Assert.Equal(PdfDPartRoot.MaxNodeVisits + 10, Assert.IsType<PdfArray>(group.RawValue).Count);
        Assert.Equal(2, Assert.IsType<PdfArray>(node.Dictionary!.Get("DParts")).Count);
    }

    [Fact]
    public void Node_budget_is_global_across_nested_groups()
    {
        var children = string.Join(" ", Enumerable.Repeat("7 0 R", PdfDPartRoot.MaxNodeVisits));
        var node = ReadNode(Load(node: "/DParts [[6 0 R 8 0 R]]", objects:
            $"6 0 obj\n<< /DParts [[{children}]] >>\nendobj\n7 0 obj\n<< >>\nendobj\n"));
        var group = Assert.Single(node.Children);
        var six = Assert.Single(group.Nodes);
        Assert.Equal(PdfDPartRoot.MaxNodeVisits - 2, Assert.Single(six.Children).Nodes.Count);
        Assert.Equal(PdfDPartBoundary.NodeLimit, group.Boundary);
        Assert.Equal(PdfDPartBoundary.NodeLimit, Assert.Single(six.Children).Boundary);
    }

    [Fact]
    public void Empty_and_malformed_groups_are_also_globally_bounded()
    {
        var groups = string.Join(" ", Enumerable.Repeat("[] null", PdfDPartRoot.MaxGroupVisits));
        var node = ReadNode(Load(node: $"/DParts [{groups}]"));
        Assert.Equal(PdfDPartRoot.MaxGroupVisits, node.Children.Count);
        Assert.Equal(PdfDPartBoundary.GroupLimit, node.ChildrenBoundary);
        Assert.All(node.Children, g => Assert.Empty(g.Nodes));
        Assert.Equal(PdfDPartRoot.MaxGroupVisits * 2, Assert.IsType<PdfArray>(node.Dictionary!.Get("DParts")).Count);
    }

    [Fact]
    public void Group_budget_is_shared_across_descendants_and_later_siblings()
    {
        var groups = string.Join(" ", Enumerable.Repeat("[]", PdfDPartRoot.MaxGroupVisits));
        var node = ReadNode(Load(node: "/DParts [[6 0 R 7 0 R] []]", objects:
            $"6 0 obj\n<< /DParts [{groups}] >>\nendobj\n7 0 obj\n<< /DParts [[]] >>\nendobj\n"));
        var group = Assert.Single(node.Children);
        Assert.Equal(2, group.Nodes.Count);
        Assert.Equal(PdfDPartRoot.MaxGroupVisits - 1, group.Nodes[0].Children.Count);
        Assert.Equal(PdfDPartBoundary.GroupLimit, group.Nodes[0].ChildrenBoundary);
        Assert.Empty(group.Nodes[1].Children);
        Assert.Equal(PdfDPartBoundary.GroupLimit, group.Nodes[1].ChildrenBoundary);
        Assert.Equal(PdfDPartBoundary.GroupLimit, node.ChildrenBoundary);
    }

    [Fact]
    public void Page_refs_and_payload_metadata_remain_raw_without_claiming_range_conformance()
    {
        var doc = Load(root: "/DPartRootNode 5 0 R /RecordLevel (bad) /NodeNameList 42",
            node: "/Start 99 2 R /End 3 0 R /Parent 123 0 R /DPM 8 0 R /Metadata 9 0 R /AF [10 0 R] /DParts [[6 0 R]]",
            objects: "6 0 obj\n<< /End 999 0 R >>\nendobj\n" +
            "8 0 obj\n<< /Opaque (kept) >>\nendobj\n" +
            "9 0 obj\n<< /Length 3 /Filter /Unsupported >>\nstream\nxyz\nendstream\nendobj\n" +
            "10 0 obj\n<< /Type /Filespec /F (never-open.pdf) >>\nendobj\n");
        var root = Read(doc);
        Assert.Null(root.RecordLevel);
        Assert.Null(root.NodeNameList);
        Assert.IsType<PdfString>(root.Dictionary!.Get("RecordLevel"));
        Assert.IsType<PdfInteger>(root.Dictionary.Get("NodeNameList"));
        var node = Assert.IsType<PdfDPartNode>(root.RootNode);
        Assert.Equal(new PdfRef(99, 2), node.Start);
        Assert.Equal(new PdfRef(3, 0), node.End);
        Assert.Equal(new PdfRef(123, 0), node.Parent);
        Assert.Equal(new PdfRef(8, 0), node.DPM);
        Assert.Equal(new PdfRef(9, 0), node.Metadata);
        Assert.Same(node.Dictionary!.Get("AF"), node.AF);
        Assert.Equal(new PdfRef(10, 0), Assert.Single(Assert.IsType<PdfArray>(node.AF).Items));
        var child = Assert.Single(Assert.Single(node.Children).Nodes);
        Assert.Null(child.Start);
        Assert.Equal(new PdfRef(999, 0), child.End);
        Assert.Equal(PdfDPartBoundary.None, node.Boundary);
        Assert.Equal(PdfDPartBoundary.None, child.Boundary);
    }

    [Fact]
    public void Malformed_node_fields_remain_available_and_direct_payloads_are_not_interpreted()
    {
        var node = ReadNode(Load(node: "/Start 42 /End null /Parent (bad) /DPM << /Vendor true >> /Metadata (raw) /AF false"));
        Assert.Null(node.Start);
        Assert.Null(node.End);
        Assert.Null(node.Parent);
        Assert.IsType<PdfInteger>(node.Dictionary!.Get("Start"));
        Assert.IsType<PdfNull>(node.Dictionary.Get("End"));
        Assert.IsType<PdfString>(node.Dictionary.Get("Parent"));
        Assert.IsType<PdfDictionary>(node.DPM);
        Assert.IsType<PdfString>(node.Metadata);
        Assert.IsType<PdfBoolean>(node.AF);
    }
}
