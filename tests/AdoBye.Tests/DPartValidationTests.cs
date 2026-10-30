using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace AdoBye.Tests;

public class DPartValidationTests
{
    static PdfDocument Load(string node = "/Parent 4 0 R /Start 10000 0 R",
        string[]? pages = null, string objects = "",
        string catalog = "/DPartRoot 4 0 R", string root = "/DPartRootNode 5 0 R")
    {
        pages ??= ["/DPart 5 0 R"];
        var pdf = new StringBuilder("%PDF-2.0\n");
        pdf.Append($"1 0 obj\n<< /Type /Catalog /Pages 2 0 R {catalog} >>\nendobj\n");
        pdf.Append($"2 0 obj\n<< /Type /Pages /Count {pages.Length} /Kids [");
        for (var i = 0; i < pages.Length; i++) pdf.Append($"{10000 + i} 0 R ");
        pdf.Append("] >>\nendobj\n");
        pdf.Append($"4 0 obj\n<< {root} >>\nendobj\n5 0 obj\n<< {node} >>\nendobj\n");
        for (var i = 0; i < pages.Length; i++)
            pdf.Append($"{10000 + i} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] {pages[i]} >>\nendobj\n");
        pdf.Append(objects).Append("trailer\n<< /Root 1 0 R /Size 20000 >>\n%%EOF\n");
        return PdfDocument.Load(Encoding.ASCII.GetBytes(pdf.ToString()));
    }

    static PdfDPartValidationReport Report(PdfDocument doc) =>
        Assert.IsType<PdfDPartValidationReport>(doc.ValidateDParts());

    static PdfDPartValidationReport HasIssue(PdfDocument doc, PdfDPartIssueCode code)
    {
        var report = Report(doc);
        Assert.False(report.IsConformant);
        Assert.Contains(report.Issues, issue => issue.Code == code);
        return report;
    }

    [Fact]
    public void Missing_optional_hierarchy_has_no_report() =>
        Assert.Null(Load(catalog: "").ValidateDParts());

    [Fact]
    public void One_page_leaf_is_conformant_without_End_or_optional_types()
    {
        var report = Report(Load());
        Assert.True(report.IsComplete);
        Assert.True(report.IsConformant);
        Assert.Empty(report.Issues);
        Assert.True(Assert.IsAssignableFrom<ICollection<PdfDPartIssue>>(report.Issues).IsReadOnly);
    }

    [Fact]
    public void Multi_page_nested_grouped_tree_follows_depth_first_order()
    {
        var report = Report(Load(node: "/Type /DPart /Parent 4 0 R /DParts [[6 0 R] [7 0 R 8 0 R]]",
            pages: ["/DPart 9 0 R", "/DPart 9 0 R", "/DPart 7 0 R"],
            root: "/Type /DPartRoot /DPartRootNode 5 0 R", objects:
            "6 0 obj\n<< /Parent 5 0 R /DParts [[9 0 R]] >>\nendobj\n" +
            "7 0 obj\n<< /Parent 5 0 R /Start 10002 0 R >>\nendobj\n" +
            "8 0 obj\n<< /Parent 5 0 R >>\nendobj\n" +
            "9 0 obj\n<< /Parent 6 0 R /Start 10000 0 R /End 10001 0 R >>\nendobj\n"));
        Assert.True(report.IsComplete);
        Assert.True(report.IsConformant);
        Assert.Empty(report.Issues);
    }

    static PdfDocument LoadLevels(string entries) =>
        Load(node: "/Parent 4 0 R /DParts [[6 0 R 7 0 R]]",
            pages: ["/DPart 8 0 R"],
            root: $"/DPartRootNode 5 0 R {entries}", objects:
            "6 0 obj\n<< /Parent 5 0 R /DParts [[8 0 R]] >>\nendobj\n" +
            "7 0 obj\n<< /Parent 5 0 R >>\nendobj\n" +
            "8 0 obj\n<< /Parent 6 0 R /Start 10000 0 R >>\nendobj\n");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RecordLevel_identifies_an_existing_zero_based_level(int level)
    {
        var report = Report(LoadLevels($"/RecordLevel {level}"));
        Assert.True(report.IsConformant);
        Assert.Equal((long)level, report.Root.RecordLevel!.Value);
    }

    [Fact]
    public void Single_node_hierarchy_has_only_level_zero()
    {
        Assert.True(Report(Load(root: "/DPartRootNode 5 0 R /RecordLevel 0 /NodeNameList [/Root]")).IsConformant);
        HasIssue(Load(root: "/DPartRootNode 5 0 R /RecordLevel 1"), PdfDPartIssueCode.RecordLevelOutOfRange);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("3")]
    [InlineData("9223372036854775807")]
    [InlineData("-9223372036854775807")]
    public void RecordLevel_must_be_less_than_actual_hierarchy_depth(string level)
    {
        var report = HasIssue(LoadLevels($"/RecordLevel {level}"), PdfDPartIssueCode.RecordLevelOutOfRange);
        Assert.True(report.IsComplete);
        Assert.Same(report.Root.Dictionary!.Get("RecordLevel"), Assert.Single(report.Issues).RawValue);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("1.0")]
    [InlineData("/One")]
    [InlineData("(1)")]
    [InlineData("[1]")]
    [InlineData("<< >>")]
    [InlineData("999 0 R")]
    public void RecordLevel_requires_an_integer_and_retains_malformed_raw_value(string value)
    {
        var report = HasIssue(LoadLevels($"/RecordLevel {value}"), PdfDPartIssueCode.InvalidRecordLevel);
        Assert.True(report.IsComplete);
        Assert.Same(report.Root.Dictionary!.Get("RecordLevel"), Assert.Single(report.Issues).RawValue);
        Assert.Null(report.Root.RecordLevel);
    }

    [Theory]
    [InlineData("/Root /Component /Leaf")]
    [InlineData("/ns:root /_child-1.0 /A#CC#81")]
    [InlineData("/#CE#94elta /#E5#AD#90 /#D0#98#D0#BC#D1#8F")]
    public void NodeNameList_accepts_XML_names_including_colons_and_Unicode(string names)
    {
        var report = Report(LoadLevels($"/NodeNameList [{names}]"));
        Assert.True(report.IsConformant);
        Assert.Equal(3, report.Root.NodeNameList!.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Root")]
    [InlineData("/Root /Component")]
    [InlineData("/Root /Component /Leaf /Extra")]
    public void NodeNameList_count_matches_maximum_depth_not_node_or_leaf_count(string names)
    {
        var report = HasIssue(LoadLevels($"/NodeNameList [{names}]"), PdfDPartIssueCode.NodeNameCountMismatch);
        Assert.True(report.IsComplete);
        Assert.Same(report.Root.Dictionary!.Get("NodeNameList"), Assert.Single(report.Issues).RawValue);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("3")]
    [InlineData("/Root")]
    [InlineData("(Root)")]
    [InlineData("<< >>")]
    [InlineData("999 0 R")]
    public void NodeNameList_requires_an_array_and_retains_malformed_raw_value(string value)
    {
        var report = HasIssue(LoadLevels($"/NodeNameList {value}"), PdfDPartIssueCode.InvalidNodeNameList);
        Assert.True(report.IsComplete);
        Assert.Same(report.Root.Dictionary!.Get("NodeNameList"), Assert.Single(report.Issues).RawValue);
        Assert.Null(report.Root.NodeNameList);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("(Root)")]
    [InlineData("[]")]
    [InlineData("<< >>")]
    [InlineData("999 0 R")]
    public void NodeNameList_entries_require_PDF_names(string value)
    {
        var report = HasIssue(LoadLevels($"/NodeNameList [/Root {value} /Leaf]"),
            PdfDPartIssueCode.InvalidNodeNameType);
        Assert.Same(report.Root.NodeNameList![1], Assert.Single(report.Issues).RawValue);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/1Root")]
    [InlineData("/-Root")]
    [InlineData("/Root#20Name")]
    [InlineData("/Root#2FName")]
    [InlineData("/Root#00Name")]
    [InlineData("/Root#3CName")]
    public void NodeNameList_rejects_empty_or_invalid_XML_names(string value)
    {
        var report = HasIssue(LoadLevels($"/NodeNameList [{value} /Component /Leaf]"),
            PdfDPartIssueCode.InvalidNodeName);
        Assert.True(report.IsComplete);
        Assert.Same(report.Root.NodeNameList![0], Assert.Single(report.Issues).RawValue);
    }

    [Fact]
    public void Root_level_metadata_accepts_indirect_values_and_name_items()
    {
        var report = Report(Load(root: "/DPartRootNode 5 0 R /RecordLevel 80 7 R /NodeNameList 81 0 R",
            objects: "80 7 obj\n0\nendobj\n81 0 obj\n[82 0 R]\nendobj\n82 0 obj\n/Root\nendobj\n"));
        Assert.True(report.IsConformant);
        Assert.IsType<PdfRef>(report.Root.Dictionary!.Get("RecordLevel"));
        Assert.IsType<PdfRef>(report.Root.NodeNameList![0]);
    }

    [Theory]
    [InlineData("/RecordLevel 80 1 R", PdfDPartIssueCode.InvalidRecordLevel)]
    [InlineData("/NodeNameList 81 1 R", PdfDPartIssueCode.InvalidNodeNameList)]
    [InlineData("/NodeNameList [82 1 R]", PdfDPartIssueCode.InvalidNodeNameType)]
    [InlineData("/RecordLevel 83 0 R", PdfDPartIssueCode.InvalidRecordLevel)]
    public void Root_level_metadata_resolution_honors_generations_and_hop_boundaries(
        string entries, PdfDPartIssueCode code) =>
        HasIssue(Load(root: $"/DPartRootNode 5 0 R {entries}",
            objects: "80 0 obj\n0\nendobj\n81 0 obj\n[/Root]\nendobj\n" +
            "82 0 obj\n/Root\nendobj\n83 0 obj\n83 0 R\nendobj\n"), code);

    [Theory]
    [InlineData("/DParts null")]
    [InlineData("/DParts [[999 0 R]]")]
    [InlineData("/DParts [[5 0 R]]")]
    public void Partial_hierarchies_leave_level_comparisons_indeterminate(string children)
    {
        var report = HasIssue(Load(node: $"/Parent 4 0 R {children}",
            root: "/DPartRootNode 5 0 R /RecordLevel 500 /NodeNameList [/Root]"),
            PdfDPartIssueCode.HierarchyBoundary);
        Assert.False(report.IsComplete);
        Assert.DoesNotContain(report.Issues, i => i.Code == PdfDPartIssueCode.RecordLevelOutOfRange ||
            i.Code == PdfDPartIssueCode.NodeNameCountMismatch);
    }

    [Fact]
    public void Incomplete_hierarchy_still_checks_optional_field_types_and_XML_names()
    {
        var report = HasIssue(Load(node: "/Parent 4 0 R /DParts null",
            root: "/DPartRootNode 5 0 R /RecordLevel (bad) /NodeNameList [/1Root 42]"),
            PdfDPartIssueCode.HierarchyBoundary);
        Assert.Contains(report.Issues, i => i.Code == PdfDPartIssueCode.InvalidRecordLevel);
        Assert.Contains(report.Issues, i => i.Code == PdfDPartIssueCode.InvalidNodeName);
        Assert.Contains(report.Issues, i => i.Code == PdfDPartIssueCode.InvalidNodeNameType);
        Assert.DoesNotContain(report.Issues, i => i.Code == PdfDPartIssueCode.NodeNameCountMismatch);
    }

    [Fact]
    public void Page_boundary_does_not_hide_definite_root_level_errors()
    {
        var doc = Load(root: "/DPartRootNode 5 0 R /RecordLevel 1 /NodeNameList []");
        Assert.IsType<PdfDictionary>(doc.File.GetObject(2)).Entries["Kids"] = PdfNull.Instance;
        var report = HasIssue(doc, PdfDPartIssueCode.PageTreeBoundary);
        Assert.Contains(report.Issues, i => i.Code == PdfDPartIssueCode.RecordLevelOutOfRange);
        Assert.Contains(report.Issues, i => i.Code == PdfDPartIssueCode.NodeNameCountMismatch);
    }

    [Fact]
    public void NodeNameList_item_inspection_is_bounded_and_retains_the_unvisited_tail()
    {
        var doc = Load();
        var names = new PdfArray(Enumerable.Repeat<PdfObject>(new PdfInteger(42),
            PdfDPartRoot.MaxNodeVisits + 1).ToList());
        Assert.IsType<PdfDictionary>(doc.File.GetObject(4)).Entries["NodeNameList"] = names;
        var report = HasIssue(doc, PdfDPartIssueCode.NodeNameListBoundary);
        Assert.False(report.IsComplete);
        Assert.Equal(PdfDPartRoot.MaxNodeVisits,
            report.Issues.Count(i => i.Code == PdfDPartIssueCode.InvalidNodeNameType));
        var boundary = Assert.Single(report.Issues, i => i.Code == PdfDPartIssueCode.NodeNameListBoundary);
        Assert.Equal(PdfDPartBoundary.NodeLimit, boundary.Boundary);
        Assert.Same(names, boundary.RawValue);
        Assert.Equal(PdfDPartRoot.MaxNodeVisits + 1, names.Count);
    }

    [Fact]
    public void Last_expandable_hierarchy_level_is_valid_for_both_optional_fields()
    {
        var objects = new StringBuilder();
        for (var depth = 1; depth < PdfDPartRoot.MaxDepth; depth++)
        {
            var number = 5 + depth;
            var entries = depth == PdfDPartRoot.MaxDepth - 1
                ? "/Start 10000 0 R" : $"/DParts [[{number + 1} 0 R]]";
            objects.Append($"{number} 0 obj\n<< /Parent {number - 1} 0 R {entries} >>\nendobj\n");
        }
        var names = string.Join(" ", Enumerable.Repeat("/Level", PdfDPartRoot.MaxDepth));
        var report = Report(Load(node: "/Parent 4 0 R /DParts [[6 0 R]]",
            pages: [$"/DPart {5 + PdfDPartRoot.MaxDepth - 1} 0 R"],
            root: $"/DPartRootNode 5 0 R /RecordLevel {PdfDPartRoot.MaxDepth - 1} /NodeNameList [{names}]",
            objects: objects.ToString()));
        Assert.True(report.IsComplete);
        Assert.True(report.IsConformant);
    }

    [Fact]
    public void Root_level_validation_preserves_inert_views_and_all_raw_entries()
    {
        var doc = Load(root: "/DPartRootNode 5 0 R /RecordLevel (bad) /NodeNameList [/1Root 42] /Vendor (kept)");
        var dictionary = Assert.IsType<PdfDictionary>(doc.File.GetObject(4));
        var before = dictionary.Entries.ToArray();
        var inert = Assert.IsType<PdfDPartRoot>(doc.GetDPartRoot());
        var report = Report(doc);
        Assert.False(report.IsConformant);
        Assert.Equal(before, dictionary.Entries.ToArray());
        Assert.Same(inert.Dictionary, report.Root.Dictionary);
        Assert.Same(inert.NodeNameList, report.Root.NodeNameList);
        Assert.Null(inert.RecordLevel);
        Assert.Equal(PdfDPartBoundary.None, inert.Boundary);
        Assert.Equal(PdfDPartBoundary.None, inert.RootNode!.Boundary);
        Assert.IsType<PdfString>(report.Root.Dictionary!.Get("RecordLevel"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Parent null")]
    [InlineData("/Parent << >>")]
    [InlineData("/Parent 999 0 R")]
    [InlineData("/Parent 4 1 R")]
    [InlineData("/Parent 5 0 R")]
    public void Root_node_requires_indirect_parent_to_DPartRoot(string parent) =>
        HasIssue(Load(node: $"{parent} /Start 10000 0 R"), PdfDPartIssueCode.InvalidParent);

    [Theory]
    [InlineData("")]
    [InlineData("/Parent 4 0 R")]
    [InlineData("/Parent 6 0 R")]
    [InlineData("/Parent (bad)")]
    public void Child_parent_must_be_immediate_ancestor(string parent) =>
        HasIssue(Load(node: "/Parent 4 0 R /DParts [[6 0 R]]",
            pages: ["/DPart 6 0 R"],
            objects: $"6 0 obj\n<< {parent} /Start 10000 0 R >>\nendobj\n"),
            PdfDPartIssueCode.InvalidParent);

    [Fact]
    public void Parent_range_and_page_links_use_resolved_dictionary_identity()
    {
        var report = Report(Load(node: "/Parent 80 0 R /Start 81 0 R",
            pages: ["/DPart 82 0 R"], objects:
            "80 0 obj\n4 0 R\nendobj\n81 0 obj\n10000 0 R\nendobj\n82 0 obj\n5 0 R\nendobj\n"));
        Assert.True(report.IsConformant);
    }

    [Theory]
    [InlineData("/Parent 80 0 R /Start 10000 0 R", "/DPart 5 0 R", PdfDPartIssueCode.InvalidParent)]
    [InlineData("/Parent 4 0 R /Start 81 0 R", "/DPart 5 0 R", PdfDPartIssueCode.InvalidStart)]
    [InlineData("/Parent 4 0 R /Start 10000 0 R", "/DPart 82 0 R", PdfDPartIssueCode.InvalidPageAssociation)]
    public void Reference_chains_must_honor_generations_at_every_hop(string node, string page, PdfDPartIssueCode code) =>
        HasIssue(Load(node: node, pages: [page], objects:
            "80 0 obj\n4 1 R\nendobj\n81 0 obj\n10000 1 R\nendobj\n82 0 obj\n5 1 R\nendobj\n"), code);

    [Fact]
    public void Matching_nonzero_reference_generations_are_accepted()
    {
        var report = Report(Load(node: "/Parent 80 7 R /Start 81 3 R",
            pages: ["/DPart 82 2 R"], objects:
            "80 7 obj\n4 0 R\nendobj\n81 3 obj\n10000 0 R\nendobj\n82 2 obj\n5 0 R\nendobj\n"));
        Assert.True(report.IsConformant);
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(33, false)]
    public void Reference_resolution_uses_the_existing_parser_hop_budget(int hops, bool conformant)
    {
        var objects = new StringBuilder();
        for (var i = 0; i < hops - 1; i++)
        {
            var next = i == hops - 2 ? 10000 : 101 + i;
            objects.Append($"{100 + i} 0 obj\n{next} 0 R\nendobj\n");
        }
        var report = Report(Load(node: "/Parent 4 0 R /Start 100 0 R", objects: objects.ToString()));
        Assert.Equal(conformant, report.IsConformant);
        if (!conformant) Assert.Contains(report.Issues, i => i.Code == PdfDPartIssueCode.InvalidStart);
    }

    [Fact]
    public void Indirect_child_arrays_preserve_the_same_conformance_rules()
    {
        var report = Report(Load(node: "/Parent 4 0 R /DParts 80 0 R",
            pages: ["/DPart 6 0 R"], objects:
            "80 0 obj\n[81 0 R]\nendobj\n81 0 obj\n[6 0 R]\nendobj\n" +
            "6 0 obj\n<< /Parent 5 0 R /Start 10000 0 R >>\nendobj\n"));
        Assert.True(report.IsConformant);
    }

    [Theory]
    [InlineData("/DParts []", PdfDPartIssueCode.EmptyChildren)]
    [InlineData("/DParts [[]]", PdfDPartIssueCode.EmptyChildGroup)]
    [InlineData("/DParts [[6 0 R]] /Start 10000 0 R", PdfDPartIssueCode.RangeOnNonLeaf)]
    [InlineData("/DParts [[6 0 R]] /End 10000 0 R", PdfDPartIssueCode.RangeOnNonLeaf)]
    [InlineData("/End 10000 0 R", PdfDPartIssueCode.EndWithoutStart)]
    [InlineData("/Start null", PdfDPartIssueCode.InvalidStart)]
    [InlineData("/Start 42", PdfDPartIssueCode.InvalidStart)]
    [InlineData("/Start << /Type /Page >>", PdfDPartIssueCode.InvalidStart)]
    [InlineData("/Start 999 0 R", PdfDPartIssueCode.InvalidStart)]
    [InlineData("/Start 4 0 R", PdfDPartIssueCode.InvalidStart)]
    [InlineData("/Start 10000 1 R", PdfDPartIssueCode.InvalidStart)]
    [InlineData("/Start 90 0 R", PdfDPartIssueCode.InvalidStart)]
    [InlineData("/Start 91 0 R", PdfDPartIssueCode.InvalidStart)]
    [InlineData("/Start 10000 0 R /End null", PdfDPartIssueCode.InvalidEnd)]
    [InlineData("/Start 10000 0 R /End 42", PdfDPartIssueCode.InvalidEnd)]
    [InlineData("/Start 10000 0 R /End << /Type /Page >>", PdfDPartIssueCode.InvalidEnd)]
    [InlineData("/Start 10000 0 R /End 999 0 R", PdfDPartIssueCode.InvalidEnd)]
    [InlineData("/Start 10000 0 R /End 4 0 R", PdfDPartIssueCode.InvalidEnd)]
    [InlineData("/Start 10000 0 R /End 10000 1 R", PdfDPartIssueCode.InvalidEnd)]
    [InlineData("/Start 10000 0 R /End 90 0 R", PdfDPartIssueCode.InvalidEnd)]
    [InlineData("/Start 10000 0 R /End 10000 0 R", PdfDPartIssueCode.RedundantEnd)]
    public void Malformed_arrays_and_range_fields_are_reported(string entries, PdfDPartIssueCode code) =>
        HasIssue(Load(node: $"/Parent 4 0 R {entries}", objects:
            "6 0 obj\n<< /Parent 5 0 R >>\nendobj\n" +
            "90 0 obj\n<< /Type /Page >>\nendobj\n91 0 obj\n91 0 R\nendobj\n"), code);

    [Fact]
    public void Reversed_endpoints_do_not_define_a_valid_range() =>
        HasIssue(Load(node: "/Parent 4 0 R /Start 10001 0 R /End 10000 0 R",
            pages: ["/DPart 5 0 R", "/DPart 5 0 R"]), PdfDPartIssueCode.ReversedRange);

    [Fact]
    public void End_is_needed_to_cover_more_than_one_page()
    {
        var report = HasIssue(Load(pages: ["/DPart 5 0 R", "/DPart 5 0 R"]),
            PdfDPartIssueCode.UncoveredPage);
        Assert.Equal(1, Assert.Single(report.Issues).PageIndex);
    }

    [Fact]
    public void Range_less_leaf_is_not_itself_invalid_but_does_not_cover_pages()
    {
        var report = HasIssue(Load(node: "/Parent 4 0 R"), PdfDPartIssueCode.UncoveredPage);
        Assert.Equal(PdfDPartIssueCode.UncoveredPage, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void Non_overlapping_complete_ranges_must_follow_depth_first_page_order()
    {
        var report = HasIssue(Load(node: "/Parent 4 0 R /DParts [[6 0 R] [7 0 R]]",
            pages: ["/DPart 7 0 R", "/DPart 6 0 R"], objects:
            "6 0 obj\n<< /Parent 5 0 R /Start 10001 0 R >>\nendobj\n" +
            "7 0 obj\n<< /Parent 5 0 R /Start 10000 0 R >>\nendobj\n"),
            PdfDPartIssueCode.PageOrder);
        Assert.Equal(PdfDPartIssueCode.PageOrder, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void Inclusive_endpoints_cannot_overlap()
    {
        var report = HasIssue(Load(node: "/Parent 4 0 R /DParts [[6 0 R 7 0 R]]",
            pages: ["/DPart 6 0 R", "/DPart 6 0 R", "/DPart 7 0 R"], objects:
            "6 0 obj\n<< /Parent 5 0 R /Start 10000 0 R /End 10001 0 R >>\nendobj\n" +
            "7 0 obj\n<< /Parent 5 0 R /Start 10001 0 R /End 10002 0 R >>\nendobj\n"),
            PdfDPartIssueCode.OverlappingRanges);
        Assert.Equal(1, Assert.Single(report.Issues, i => i.Code == PdfDPartIssueCode.OverlappingRanges).PageIndex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/DPart null")]
    [InlineData("/DPart << /Parent 4 0 R /Start 10000 0 R >>")]
    [InlineData("/DPart 999 0 R")]
    [InlineData("/DPart 4 0 R")]
    [InlineData("/DPart 5 1 R")]
    public void Each_page_requires_an_indirect_link_to_its_owning_leaf(string association)
    {
        var report = HasIssue(Load(pages: [association]), PdfDPartIssueCode.InvalidPageAssociation);
        var issue = Assert.Single(report.Issues);
        Assert.Equal(0, issue.PageIndex);
        Assert.Same(report.Root.RootNode, issue.Node);
    }

    [Fact]
    public void Interior_pages_also_require_the_owning_leaf_not_its_ancestor()
    {
        var report = HasIssue(Load(node: "/Parent 4 0 R /DParts [[6 0 R]]",
            pages: ["/DPart 6 0 R", "/DPart 5 0 R", "/DPart 6 0 R"], objects:
            "6 0 obj\n<< /Parent 5 0 R /Start 10000 0 R /End 10002 0 R >>\nendobj\n"),
            PdfDPartIssueCode.InvalidPageAssociation);
        Assert.Equal(1, Assert.Single(report.Issues).PageIndex);
    }

    [Theory]
    [InlineData("/DParts [null]", PdfDPartBoundary.InvalidChildren)]
    [InlineData("/DParts null", PdfDPartBoundary.InvalidChildren)]
    [InlineData("/DParts [[42]]", PdfDPartBoundary.InvalidReference)]
    [InlineData("/DParts [[999 0 R]]", PdfDPartBoundary.InvalidDictionary)]
    [InlineData("/DParts [[6 1 R]]", PdfDPartBoundary.InvalidReference)]
    [InlineData("/DParts 80 1 R", PdfDPartBoundary.InvalidChildren)]
    [InlineData("/DParts [81 1 R]", PdfDPartBoundary.InvalidChildren)]
    [InlineData("/Type /Page", PdfDPartBoundary.InvalidType)]
    [InlineData("/DParts [[5 0 R]]", PdfDPartBoundary.Cycle)]
    [InlineData("/DParts [[6 0 R 6 0 R]]", PdfDPartBoundary.RepeatedReference)]
    public void Malformed_or_incomplete_hierarchy_keeps_boundaries(string entries, PdfDPartBoundary boundary)
    {
        var report = HasIssue(Load(node: $"/Parent 4 0 R {entries}", objects:
            "6 0 obj\n<< /Parent 5 0 R /Start 10000 0 R >>\nendobj\n" +
            "80 0 obj\n[[6 0 R]]\nendobj\n81 0 obj\n[6 0 R]\nendobj\n"),
            PdfDPartIssueCode.HierarchyBoundary);
        Assert.False(report.IsComplete);
        Assert.Contains(report.Issues, i => i.Boundary == boundary);
        Assert.NotNull(report.Root.RootNode!.Dictionary!.Get(
            entries.StartsWith("/Type", StringComparison.Ordinal) ? "Type" : "DParts"));
    }

    [Fact]
    public void Reusing_a_child_under_two_parents_reports_reuse_and_disagreement()
    {
        var report = HasIssue(Load(node: "/Parent 4 0 R /DParts [[6 0 R 7 0 R]]",
            pages: ["/DPart 8 0 R"], objects:
            "6 0 obj\n<< /Parent 5 0 R /DParts [[8 0 R]] >>\nendobj\n" +
            "7 0 obj\n<< /Parent 5 0 R /DParts [[8 0 R]] >>\nendobj\n" +
            "8 0 obj\n<< /Parent 6 0 R /Start 10000 0 R >>\nendobj\n"),
            PdfDPartIssueCode.InvalidParent);
        Assert.Contains(report.Issues, i => i.Boundary == PdfDPartBoundary.RepeatedReference);
        Assert.False(report.IsComplete);
    }

    [Theory]
    [InlineData("/DPartRoot null", "/DPartRootNode 5 0 R", PdfDPartBoundary.InvalidReference)]
    [InlineData("/DPartRoot 999 0 R", "/DPartRootNode 5 0 R", PdfDPartBoundary.InvalidDictionary)]
    [InlineData("/DPartRoot 4 1 R", "/DPartRootNode 5 0 R", PdfDPartBoundary.InvalidReference)]
    [InlineData("/DPartRoot 4 0 R", "/Type /Page /DPartRootNode 5 0 R", PdfDPartBoundary.InvalidType)]
    [InlineData("/DPartRoot 4 0 R", "", PdfDPartBoundary.InvalidReference)]
    [InlineData("/DPartRoot 4 0 R", "/DPartRootNode 5 1 R", PdfDPartBoundary.InvalidReference)]
    public void Invalid_root_cannot_be_conformant(string catalog, string root, PdfDPartBoundary boundary)
    {
        var report = HasIssue(Load(catalog: catalog, root: root), PdfDPartIssueCode.HierarchyBoundary);
        Assert.False(report.IsComplete);
        Assert.Contains(report.Issues, i => i.Boundary == boundary);
    }

    [Theory]
    [InlineData(PdfDPartBoundary.DepthLimit)]
    [InlineData(PdfDPartBoundary.NodeLimit)]
    [InlineData(PdfDPartBoundary.GroupLimit)]
    public void Existing_hierarchy_budgets_are_visible_in_report(PdfDPartBoundary boundary)
    {
        string entries;
        var objects = new StringBuilder("6 0 obj\n<< /Parent 5 0 R >>\nendobj\n");
        if (boundary == PdfDPartBoundary.DepthLimit)
        {
            objects.Clear();
            entries = "/DParts [[6 0 R]]";
            for (var i = 6; i <= 6 + PdfDPartRoot.MaxDepth; i++)
                objects.Append($"{i} 0 obj\n<< /Parent {i - 1} 0 R /DParts [[{i + 1} 0 R]] >>\nendobj\n");
        }
        else if (boundary == PdfDPartBoundary.NodeLimit)
            entries = $"/DParts [[{string.Join(" ", Enumerable.Repeat("6 0 R", PdfDPartRoot.MaxNodeVisits))}]]";
        else
            entries = $"/DParts [{string.Join(" ", Enumerable.Repeat("[]", PdfDPartRoot.MaxGroupVisits + 1))}]";
        var report = Report(Load(node: $"/Parent 4 0 R {entries}", objects: objects.ToString(),
            root: "/DPartRootNode 5 0 R /RecordLevel 500 /NodeNameList [/Root]"));
        Assert.False(report.IsComplete);
        Assert.False(report.IsConformant);
        Assert.Contains(report.Issues, i => i.Boundary == boundary);
        Assert.DoesNotContain(report.Issues, i => i.Code == PdfDPartIssueCode.UncoveredPage);
        Assert.DoesNotContain(report.Issues, i => i.Code == PdfDPartIssueCode.RecordLevelOutOfRange ||
            i.Code == PdfDPartIssueCode.NodeNameCountMismatch);
    }

    [Theory]
    [InlineData("10000 0 R 10000 0 R", PdfDPartBoundary.RepeatedReference)]
    [InlineData("999 0 R", PdfDPartBoundary.InvalidDictionary)]
    [InlineData("null", PdfDPartBoundary.InvalidReference)]
    [InlineData("2 0 R", PdfDPartBoundary.RepeatedReference)]
    public void Partial_page_tree_does_not_claim_complete_coverage(string kids, PdfDPartBoundary boundary)
    {
        var doc = Load();
        Assert.IsType<PdfDictionary>(doc.File.GetObject(2)).Entries["Kids"] = new PdfArray(
            kids == "10000 0 R 10000 0 R" ? [new PdfRef(10000, 0), new PdfRef(10000, 0)] :
            kids == "999 0 R" ? [new PdfRef(999, 0)] :
            kids == "2 0 R" ? [new PdfRef(2, 0)] : [PdfNull.Instance]);
        var report = HasIssue(doc, PdfDPartIssueCode.PageTreeBoundary);
        Assert.False(report.IsComplete);
        Assert.Contains(report.Issues, i => i.Code == PdfDPartIssueCode.PageTreeBoundary && i.Boundary == boundary);
    }

    [Fact]
    public void Page_slot_budget_counts_repeats_and_preserves_unvisited_tail()
    {
        var doc = Load();
        var kids = new PdfArray(Enumerable.Repeat<PdfObject>(new PdfRef(10000, 0),
            PdfDPartRoot.MaxNodeVisits + 1).ToList());
        Assert.IsType<PdfDictionary>(doc.File.GetObject(2)).Entries["Kids"] = kids;
        var report = HasIssue(doc, PdfDPartIssueCode.PageTreeBoundary);
        Assert.False(report.IsComplete);
        Assert.Same(kids, Assert.Single(report.Issues, i =>
            i.Code == PdfDPartIssueCode.PageTreeBoundary && i.Boundary == PdfDPartBoundary.NodeLimit).RawValue);
        Assert.Equal(PdfDPartRoot.MaxNodeVisits + 1, kids.Count);
    }

    [Fact]
    public void Page_tree_depth_is_bounded()
    {
        var objects = new StringBuilder();
        for (var i = 6; i <= 6 + PdfDPartRoot.MaxDepth; i++)
        {
            var child = i == 6 + PdfDPartRoot.MaxDepth ? 10000 : i + 1;
            objects.Append($"{i} 0 obj\n<< /Type /Pages /Kids [{child} 0 R] /Count 1 >>\nendobj\n");
        }
        var doc = Load(objects: objects.ToString());
        Assert.IsType<PdfDictionary>(doc.File.GetObject(2)).Entries["Kids"] = new PdfArray([new PdfRef(6, 0)]);
        var report = HasIssue(doc, PdfDPartIssueCode.PageTreeBoundary);
        Assert.False(report.IsComplete);
        Assert.Contains(report.Issues, i => i.Boundary == PdfDPartBoundary.DepthLimit);
    }

    [Fact]
    public void Direct_page_tree_children_cannot_be_validated_using_the_mutable_loaded_page_list()
    {
        var doc = Load();
        PdfDictionary next = Assert.IsType<PdfDictionary>(doc.File.GetObject(10000));
        Assert.IsType<PdfDictionary>(doc.File.GetObject(2)).Entries["Kids"] = new PdfArray([next]);
        doc.Pages.Clear();
        var report = HasIssue(doc, PdfDPartIssueCode.PageTreeBoundary);
        Assert.False(report.IsComplete);
        Assert.Contains(report.Issues, i => i.Boundary == PdfDPartBoundary.InvalidReference);
    }

    [Fact]
    public void Page_order_is_taken_from_the_page_tree_not_the_mutable_loaded_page_list()
    {
        var doc = Load(node: "/Parent 4 0 R /Start 10000 0 R /End 10001 0 R",
            pages: ["/DPart 5 0 R", "/DPart 5 0 R"]);
        doc.Pages.Reverse();
        Assert.True(Report(doc).IsConformant);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Pages")]
    [InlineData("Other")]
    public void Unreadable_page_tree_children_prevent_a_complete_report(string? type)
    {
        var doc = Load();
        var dictionary = Assert.IsType<PdfDictionary>(doc.File.GetObject(2));
        dictionary.Entries["Type"] = type is null ? PdfNull.Instance : new PdfName(type);
        dictionary.Entries["Kids"] = PdfNull.Instance;
        var report = HasIssue(doc, PdfDPartIssueCode.PageTreeBoundary);
        Assert.False(report.IsComplete);
        Assert.Contains(report.Issues, i => i.Boundary == PdfDPartBoundary.InvalidChildren);
    }

    [Fact]
    public void Maximum_page_visit_budget_includes_internal_page_tree_nodes()
    {
        var doc = Load(node: $"/Parent 4 0 R /Start 10000 0 R /End {10000 + PdfDPartRoot.MaxNodeVisits - 2} 0 R",
            pages: Enumerable.Repeat("/DPart 5 0 R", PdfDPartRoot.MaxNodeVisits - 1).ToArray());
        Assert.True(Report(doc).IsConformant);
        Assert.IsType<PdfDictionary>(doc.File.GetObject(2)).Entries["Kids"] =
            new PdfArray([.. Enumerable.Range(10000, PdfDPartRoot.MaxNodeVisits - 1).Select(i => new PdfRef(i, 0)),
                new PdfRef(10000, 0)]);
        var report = HasIssue(doc, PdfDPartIssueCode.PageTreeBoundary);
        Assert.False(report.IsComplete);
        Assert.Contains(report.Issues, i => i.Boundary == PdfDPartBoundary.NodeLimit);
    }

    [Fact]
    public void Many_fully_overlapping_ranges_produce_one_coverage_diagnostic_per_page()
    {
        const int pageCount = 128;
        var objects = new StringBuilder();
        var children = new StringBuilder();
        for (var i = 6; i < 6 + PdfDPartRoot.MaxNodeVisits - 1; i++)
        {
            children.Append($"{i} 0 R ");
            objects.Append($"{i} 0 obj\n<< /Parent 5 0 R /Start 10000 0 R /End {10000 + pageCount - 1} 0 R >>\nendobj\n");
        }
        var report = HasIssue(Load(node: $"/Parent 4 0 R /DParts [[{children}]]",
            pages: Enumerable.Repeat("/DPart 6 0 R", pageCount).ToArray(), objects: objects.ToString()),
            PdfDPartIssueCode.OverlappingRanges);
        Assert.True(report.IsComplete);
        Assert.Equal(pageCount, report.Issues.Count(i => i.Code == PdfDPartIssueCode.OverlappingRanges));
        Assert.Equal(PdfDPartRoot.MaxNodeVisits - 2, report.Issues.Count(i => i.Code == PdfDPartIssueCode.PageOrder));
        Assert.DoesNotContain(report.Issues, i => i.Code == PdfDPartIssueCode.UncoveredPage);
    }

    [Fact]
    public void Report_and_inert_reader_preserve_malformed_raw_values()
    {
        var doc = Load(node: "/Parent (bad) /DParts [[]] /Start null /End 42 /Vendor (kept)");
        var dictionaries = new[] { doc.File.Catalog, Assert.IsType<PdfDictionary>(doc.File.GetObject(4)),
            Assert.IsType<PdfDictionary>(doc.File.GetObject(5)), doc.Pages[0].Dict };
        var before = dictionaries.Select(d => d.Entries.ToArray()).ToArray();
        var inert = Assert.IsType<PdfDPartRoot>(doc.GetDPartRoot());
        var report = Report(doc);
        Assert.False(report.IsConformant);
        for (var i = 0; i < dictionaries.Length; i++)
            Assert.Equal(before[i], dictionaries[i].Entries.ToArray());
        Assert.Same(inert.RawValue, report.Root.RawValue);
        Assert.Same(inert.RootNode!.Dictionary, report.Root.RootNode!.Dictionary);
        Assert.IsType<PdfNull>(report.Root.RootNode.Dictionary!.Get("Start"));
        Assert.Equal(PdfDPartBoundary.None, inert.RootNode.Boundary);
        Assert.Empty(Assert.Single(inert.RootNode.Children).Nodes);
    }
}
