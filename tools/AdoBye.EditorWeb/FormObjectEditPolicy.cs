using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;

namespace AdoBye.EditorWeb;

internal readonly record struct FormObjectRange(int Form, int Start, int End);

internal static class FormObjectEditPolicy
{
    public static HashSet<int> FindSharedForms(PdfDocument document)
    {
        var counts = new Dictionary<int, int>();
        var invocations = new Dictionary<int, int>();
        var descendants = new Dictionary<int, HashSet<int>>();
        var scanned = new HashSet<int>();
        var pending = new Stack<(int Number, PdfStream Stream, PdfDictionary Resources)>();
        void CountForms(PdfDictionary resources, int owner = 0)
        {
            if (document.File.Resolve(resources.Get("XObject")) is not PdfDictionary xobjects) return;
            foreach (var value in xobjects.Entries.Values)
            {
                if (value is not PdfRef reference || document.File.Resolve(value) is not PdfStream stream
                    || document.File.Resolve(stream.Dict.Get("Subtype")).AsName() != "Form") continue;
                counts[reference.Number] = counts.GetValueOrDefault(reference.Number) + 1;
                if (!descendants.TryGetValue(owner, out var children)) descendants[owner] = children = [];
                children.Add(reference.Number);
                if (scanned.Add(reference.Number))
                    pending.Push((reference.Number, stream, document.File.Resolve(stream.Dict.Get("Resources")) as PdfDictionary ?? resources));
            }
        }

        void CountInvocations(byte[] content, PdfDictionary resources)
        {
            var lexer = new PdfLexer(content);
            PdfObject? operand = null;
            while (lexer.ReadObject(false) is { } token)
            {
                if (token is not PdfKeyword keyword) { operand = token; continue; }
                if (keyword.Value == "BI")
                {
                    // Inline-image bytes are not PDF operators. Conservatively withhold this
                    // stream's forms rather than guessing where binary image data ends.
                    if (document.File.Resolve(resources.Get("XObject")) is PdfDictionary images)
                        foreach (var imageRef in images.Entries.Values.OfType<PdfRef>())
                            invocations[imageRef.Number] = 2;
                    break;
                }
                if (keyword.Value == "Do" && operand is PdfName name
                    && document.File.Resolve(resources.Get("XObject")) is PdfDictionary xobjects
                    && xobjects.Get(name.Value) is PdfRef reference)
                    invocations[reference.Number] = invocations.GetValueOrDefault(reference.Number) + 1;
                operand = null;
            }
        }

        foreach (var page in document.Pages)
        {
            CountForms(page.Resources);
            CountInvocations(page.GetContents(), page.Resources);
        }
        while (pending.TryPop(out var form))
        {
            CountForms(form.Resources, form.Number);
            CountInvocations(document.File.DecodeStream(form.Stream), form.Resources);
        }

        var shared = counts.Where(kv => kv.Value > 1).Select(kv => kv.Key)
            .Concat(invocations.Where(kv => kv.Value > 1).Select(kv => kv.Key)).ToHashSet();
        var work = new Stack<int>(shared);
        while (work.TryPop(out var owner))
            if (descendants.TryGetValue(owner, out var children))
                foreach (var child in children)
                    if (shared.Add(child)) work.Push(child);
        return shared;
    }

    public static HashSet<FormObjectRange> FindAmbiguous(
        IEnumerable<ContentInterpreter.PageObject> objects, IReadOnlySet<int> sharedForms)
    {
        var pageObjects = objects.ToList();
        var directForms = pageObjects.Where(obj => obj.Form == 0 && obj.Kind == "form").Select(obj => obj.Ref).ToHashSet();
        var ranges = pageObjects.Where(obj => obj.Form != 0).GroupBy(obj => new FormObjectRange(obj.Form, obj.Start, obj.End));
        return ranges.Where(group => sharedForms.Contains(group.Key.Form) || !directForms.Contains(group.Key.Form)
                || group.Any(obj => obj.Kind == "form") || group.Count() > 1)
            .Select(group => group.Key).ToHashSet();
    }

    public static bool IsEditable(ContentInterpreter.PageObject obj, IReadOnlySet<FormObjectRange> ambiguous) =>
        obj.Form == 0 || !ambiguous.Contains(new FormObjectRange(obj.Form, obj.Start, obj.End));
}
