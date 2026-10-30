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
        Assert.Equal(1, Assert.Single(report.Issues.Where(i => i.Code == PdfDPartIssueCode.OverlappingRanges)).PageIndex);
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
        var report = Report(Load(node: $"/Parent 4 0 R {entries}", objects: objects.ToString()));
        Assert.False(report.IsComplete);
        Assert.False(report.IsConformant);
        Assert.Contains(report.Issues, i => i.Boundary == boundary);
        Assert.DoesNotContain(report.Issues, i => i.Code == PdfDPartIssueCode.UncoveredPage);
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
        Assert.Same(kids, Assert.Single(report.Issues.Where(i =>
            i.Code == PdfDPartIssueCode.PageTreeBoundary && i.Boundary == PdfDPartBoundary.NodeLimit)).RawValue);
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
