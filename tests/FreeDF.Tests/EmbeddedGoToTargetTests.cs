using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class EmbeddedGoToTargetTests
{
    static PdfDocument LoadTarget(string target, string objects = "")
    {
        var pdf = "%PDF-2.0\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /OpenAction 4 0 R >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>\nendobj\n" +
            $"4 0 obj\n<< /S /GoToE /D /Unresolved /T {target} >>\nendobj\n" +
            objects +
            "trailer\n<< /Root 1 0 R /Size 1000 >>\n%%EOF\n";
        return PdfDocument.Load(Encoding.ASCII.GetBytes(pdf));
    }

    static PdfEmbeddedGoToTarget ReadTarget(PdfDocument doc)
    {
        var action = Assert.Single(doc.GetActions());
        var details = Assert.IsType<PdfEmbeddedGoToAction>(action.EmbeddedGoToDetails);
        var target = Assert.IsType<PdfEmbeddedGoToTarget>(details.TargetPath);
        Assert.Same(action.Dictionary, details.Dictionary);
        Assert.Same(details.Target, target.Dictionary);
        Assert.Same(doc.File.Resolve(details.Dictionary.Get("T")), target.Dictionary);
        Assert.Equal("Unresolved", Assert.IsType<PdfName>(details.Destination).Value);
        return target;
    }

    [Theory]
    [InlineData("P")]
    [InlineData("C")]
    public void Parent_and_child_relationships_are_exposed_without_inferring_required_fields(string relationship)
    {
        var target = ReadTarget(LoadTarget($"<< /R /{relationship} >>"));

        Assert.Equal(relationship, target.Relationship?.Value);
        Assert.Same(target.Dictionary.Get("R"), target.Relationship);
        Assert.Null(target.Name);
        Assert.Null(target.Page);
        Assert.Null(target.Annotation);
        Assert.Null(target.NestedTarget);
        Assert.Equal(PdfEmbeddedGoToTargetBoundary.None, target.NestedTargetBoundary);
    }

    [Theory]
    [InlineData(false, "P")]
    [InlineData(false, "C")]
    [InlineData(true, "P")]
    [InlineData(true, "C")]
    public void String_fields_preserve_bytes_and_resolve_only_local_references_without_conditional_validation(bool indirect, string relationship)
    {
        var doc = LoadTarget(indirect ? "5 0 R" :
            $"<< /R /{relationship} /N <00ff8041> /P <70616765ff> /A <feff00410042> >>",
            "5 0 obj\n<< /R 6 0 R /N 7 0 R /P 8 0 R /A 9 0 R >>\nendobj\n" +
            $"6 0 obj\n/{relationship}\nendobj\n" +
            "7 0 obj\n<00ff8041>\nendobj\n" +
            "8 0 obj\n<70616765ff>\nendobj\n" +
            "9 0 obj\n<feff00410042>\nendobj\n");
        var dictionary = Assert.IsType<PdfDictionary>(
            doc.File.Resolve(Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.File.Catalog.Get("OpenAction"))).Get("T")));
        var entries = dictionary.Entries.ToArray();
        var target = ReadTarget(doc);

        Assert.Equal(relationship, target.Relationship?.Value);
        Assert.Equal(new byte[] { 0, 255, 128, 65 }, Assert.IsType<PdfString>(target.Name).Bytes);
        Assert.Equal(new byte[] { 112, 97, 103, 101, 255 }, Assert.IsType<PdfString>(target.Page).Bytes);
        Assert.Equal(new byte[] { 254, 255, 0, 65, 0, 66 }, Assert.IsType<PdfString>(target.Annotation).Bytes);
        Assert.Equal(entries, dictionary.Entries.ToArray());
        Assert.Same(doc.File.Resolve(dictionary.Get("R")), target.Relationship);
        Assert.Same(doc.File.Resolve(dictionary.Get("N")), target.Name);
        Assert.Same(doc.File.Resolve(dictionary.Get("P")), target.Page);
        Assert.Same(doc.File.Resolve(dictionary.Get("A")), target.Annotation);
        if (indirect)
            foreach (var key in new[] { "R", "N", "P", "A" })
                Assert.IsType<PdfRef>(dictionary.Get(key));
    }

    [Theory]
    [InlineData("0", "1", false)]
    [InlineData("-1", "-2", false)]
    [InlineData("9223372036854775807", "2147483648", true)]
    public void Attachment_integers_are_retained_without_page_or_annotation_lookup(string page, string annotation, bool indirect)
    {
        var target = ReadTarget(LoadTarget(
            $"<< /R /C /P {(indirect ? "5 0 R" : page)} /A {(indirect ? "6 0 R" : annotation)} >>",
            $"5 0 obj\n{page}\nendobj\n6 0 obj\n{annotation}\nendobj\n"));

        Assert.Equal(long.Parse(page), Assert.IsType<PdfInteger>(target.Page).Value);
        Assert.Equal(long.Parse(annotation), Assert.IsType<PdfInteger>(target.Annotation).Value);
        Assert.Null(target.Name);
        Assert.Equal(PdfEmbeddedGoToTargetBoundary.None, target.NestedTargetBoundary);
    }

    [Fact]
    public void Mixed_direct_and_indirect_nested_paths_retain_each_original_dictionary_and_reference()
    {
        var doc = LoadTarget("5 0 R",
            "5 0 obj\n<< /R /P /T << /R /C /N (child.pdf) /T 6 0 R >> >>\nendobj\n" +
            "6 0 obj\n<< /R /C /P (page-label) /A (annotation-name) >>\nendobj\n");
        var rootDictionary = Assert.IsType<PdfDictionary>(doc.File.Resolve(new PdfRef(5, 0)));
        var childDictionary = Assert.IsType<PdfDictionary>(rootDictionary.Get("T"));
        var leafDictionary = Assert.IsType<PdfDictionary>(doc.File.Resolve(childDictionary.Get("T")));
        var rootEntries = rootDictionary.Entries.ToArray();
        var childEntries = childDictionary.Entries.ToArray();
        var leafEntries = leafDictionary.Entries.ToArray();

        var root = ReadTarget(doc);
        var child = Assert.IsType<PdfEmbeddedGoToTarget>(root.NestedTarget);
        var leaf = Assert.IsType<PdfEmbeddedGoToTarget>(child.NestedTarget);

        Assert.Same(rootDictionary, root.Dictionary);
        Assert.Same(childDictionary, child.Dictionary);
        Assert.Same(leafDictionary, leaf.Dictionary);
        Assert.Equal("P", root.Relationship?.Value);
        Assert.Equal("C", child.Relationship?.Value);
        Assert.Equal("child.pdf", child.Name?.Text);
        Assert.Equal("page-label", Assert.IsType<PdfString>(leaf.Page).Text);
        Assert.Equal("annotation-name", Assert.IsType<PdfString>(leaf.Annotation).Text);
        Assert.Null(leaf.NestedTarget);
        Assert.All(new[] { root, child, leaf },
            node => Assert.Equal(PdfEmbeddedGoToTargetBoundary.None, node.NestedTargetBoundary));
        Assert.Equal(rootEntries, rootDictionary.Entries.ToArray());
        Assert.Equal(childEntries, childDictionary.Entries.ToArray());
        Assert.Equal(leafEntries, leafDictionary.Entries.ToArray());
        Assert.IsType<PdfRef>(child.Dictionary.Get("T"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Direct_dictionary_cycles_are_bounded_by_identity_and_keep_the_cycle_edge(bool multipleNodes)
    {
        var doc = LoadTarget("<< /R /P >>");
        var action = Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.File.Catalog.Get("OpenAction")));
        var rootDictionary = Assert.IsType<PdfDictionary>(action.Get("T"));
        var lastDictionary = multipleNodes
            ? new PdfDictionary(new() { ["R"] = new PdfName("C") })
            : rootDictionary;
        rootDictionary.Entries["T"] = lastDictionary;
        lastDictionary.Entries["T"] = rootDictionary;

        var root = ReadTarget(doc);
        var last = multipleNodes ? Assert.IsType<PdfEmbeddedGoToTarget>(root.NestedTarget) : root;

        if (multipleNodes)
            Assert.Equal(PdfEmbeddedGoToTargetBoundary.None, root.NestedTargetBoundary);
        Assert.Null(last.NestedTarget);
        Assert.Equal(PdfEmbeddedGoToTargetBoundary.Cycle, last.NestedTargetBoundary);
        Assert.Same(lastDictionary, last.Dictionary);
        Assert.Same(rootDictionary, last.Dictionary.Get("T"));
    }

    [Theory]
    [InlineData("5 0 R", false)]
    [InlineData("6 0 R", true)]
    [InlineData("7 0 R", false)]
    public void Indirect_self_and_multi_node_cycles_report_the_unexpanded_raw_edge(string next, bool multipleNodes)
    {
        var doc = LoadTarget("5 0 R",
            $"5 0 obj\n<< /R /P /T {next} >>\nendobj\n" +
            "6 0 obj\n<< /R /C /T 5 0 R >>\nendobj\n" +
            "7 0 obj\n5 0 R\nendobj\n");
        var root = ReadTarget(doc);
        var last = multipleNodes ? Assert.IsType<PdfEmbeddedGoToTarget>(root.NestedTarget) : root;

        Assert.Null(last.NestedTarget);
        Assert.Equal(PdfEmbeddedGoToTargetBoundary.Cycle, last.NestedTargetBoundary);
        Assert.IsType<PdfRef>(last.Dictionary.Get("T"));
        Assert.Same(root.Dictionary, doc.File.Resolve(last.Dictionary.Get("T")));
        var repeated = ReadTarget(doc);
        Assert.Equal(root.NestedTargetBoundary, repeated.NestedTargetBoundary);
        Assert.Equal(last.NestedTargetBoundary, (repeated.NestedTarget ?? repeated).NestedTargetBoundary);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(PdfEmbeddedGoToTarget.MaxPathLength)]
    [InlineData(PdfEmbeddedGoToTarget.MaxPathLength + 1)]
    [InlineData(PdfEmbeddedGoToTarget.MaxPathLength + 2)]
    public void Depth_bound_is_exact_and_preserves_the_unexpanded_dictionary_reference(int count)
    {
        var objects = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            var next = i + 1 < count ? $"/T {i + 6} 0 R" : "";
            objects.Append($"{i + 5} 0 obj\n<< /R /C /N (target-{i}) {next} >>\nendobj\n");
        }
        var doc = LoadTarget("5 0 R", objects.ToString());
        var node = ReadTarget(doc);
        var exposed = Math.Min(count, PdfEmbeddedGoToTarget.MaxPathLength);
        for (var i = 0; i < exposed; i++)
        {
            Assert.Equal($"target-{i}", node.Name?.Text);
            Assert.Same(doc.File.Resolve(new PdfRef(i + 5, 0)), node.Dictionary);
            if (i + 1 < exposed)
            {
                Assert.Equal(PdfEmbeddedGoToTargetBoundary.None, node.NestedTargetBoundary);
                node = Assert.IsType<PdfEmbeddedGoToTarget>(node.NestedTarget);
            }
        }

        Assert.Null(node.NestedTarget);
        Assert.Equal(count > exposed ? PdfEmbeddedGoToTargetBoundary.DepthLimit : PdfEmbeddedGoToTargetBoundary.None,
            node.NestedTargetBoundary);
        if (count > exposed)
        {
            Assert.IsType<PdfRef>(node.Dictionary.Get("T"));
            var unexpanded = Assert.IsType<PdfDictionary>(doc.File.Resolve(node.Dictionary.Get("T")));
            Assert.Equal($"target-{exposed}", Assert.IsType<PdfString>(unexpanded.Get("N")).Text);
        }
        else
            Assert.Null(node.Dictionary.Get("T"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/R null /N null /P null /A null /T null")]
    [InlineData("/R (C) /N /Name /P 1.5 /A true /T []")]
    [InlineData("/R 42 /N 42 /P /Page /A /Annotation /T (child)")]
    [InlineData("/R [] /N [] /P [] /A [] /T /C")]
    [InlineData("/R << >> /N << >> /P << >> /A << >> /T false")]
    [InlineData("/R 8 0 R /N 8 0 R /P 8 0 R /A 8 0 R /T 8 0 R")]
    [InlineData("/R 9 0 R /N 9 0 R /P 9 0 R /A 9 0 R /T 9 0 R")]
    [InlineData("/R 6 0 R /N 6 0 R /P 6 0 R /A 6 0 R /T 6 0 R")]
    [InlineData("/R 7 0 R /N 7 0 R /P 7 0 R /A 7 0 R /T 7 0 R")]
    public void Malformed_fields_are_null_without_losing_raw_values_or_unknown_entries(string entries)
    {
        var doc = LoadTarget($"<< {entries} /Vendor << /Opaque 8 0 R >> /Unknown /Keep >>",
            "6 0 obj\n6 0 R\nendobj\n" +
            "7 0 obj\nfalse\nendobj\n" +
            "8 0 obj\n<< /Filter /Unsupported /Length 4 >>\nstream\ndata\nendstream\nendobj\n");
        var action = Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.File.Catalog.Get("OpenAction")));
        var dictionary = Assert.IsType<PdfDictionary>(action.Get("T"));
        var originalEntries = dictionary.Entries.ToArray();
        var target = ReadTarget(doc);

        Assert.Null(target.Relationship);
        Assert.Null(target.Name);
        Assert.Null(target.Page);
        Assert.Null(target.Annotation);
        Assert.Null(target.NestedTarget);
        Assert.Equal(PdfEmbeddedGoToTargetBoundary.None, target.NestedTargetBoundary);
        Assert.Same(dictionary, target.Dictionary);
        Assert.Equal(originalEntries, target.Dictionary.Entries.ToArray());
        Assert.Equal("Keep", Assert.IsType<PdfName>(target.Dictionary.Get("Unknown")).Value);
        Assert.IsType<PdfRef>(Assert.IsType<PdfDictionary>(target.Dictionary.Get("Vendor")).Get("Opaque"));
    }

    [Fact]
    public void Unknown_relationships_and_nested_unknown_entries_are_retained_without_interpretation()
    {
        var target = ReadTarget(LoadTarget(
            "<< /R /Vendor /N (unopened.pdf) /P -1 /A (no-lookup) /T << /R /Other /Extension [1 /Raw] >> >>"));

        Assert.Equal("Vendor", target.Relationship?.Value);
        Assert.Equal("unopened.pdf", target.Name?.Text);
        Assert.Equal(-1L, Assert.IsType<PdfInteger>(target.Page).Value);
        Assert.Equal("no-lookup", Assert.IsType<PdfString>(target.Annotation).Text);
        var child = Assert.IsType<PdfEmbeddedGoToTarget>(target.NestedTarget);
        Assert.Equal("Other", child.Relationship?.Value);
        Assert.IsType<PdfArray>(child.Dictionary.Get("Extension"));
        Assert.Same(target.Dictionary.Get("T"), child.Dictionary);
    }

    [Fact]
    public void Existing_action_constructor_and_deconstruction_remain_usable()
    {
        var dictionary = new PdfDictionary();
        var target = new PdfDictionary();
        var details = new PdfEmbeddedGoToAction(null, null, target, null, dictionary);
        var (file, destination, rawTarget, newWindow, rawAction) = details;

        Assert.Null(file);
        Assert.Null(destination);
        Assert.Same(target, rawTarget);
        Assert.Null(newWindow);
        Assert.Same(dictionary, rawAction);
        Assert.Null(details.TargetPath);
    }
}
