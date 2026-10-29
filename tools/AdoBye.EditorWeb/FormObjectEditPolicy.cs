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
        var scanned = new HashSet<int>();
        var pending = new Stack<PdfDictionary>();
        void CountForms(PdfDictionary? resources)
        {
            if (document.File.Resolve(resources?.Get("XObject")) is not PdfDictionary xobjects) return;
            foreach (var value in xobjects.Entries.Values)
            {
                if (value is not PdfRef reference || document.File.Resolve(value) is not PdfStream stream
                    || document.File.Resolve(stream.Dict.Get("Subtype")).AsName() != "Form") continue;
                counts[reference.Number] = counts.GetValueOrDefault(reference.Number) + 1;
                if (scanned.Add(reference.Number) && document.File.Resolve(stream.Dict.Get("Resources")) is PdfDictionary nested)
                    pending.Push(nested);
            }
        }

        foreach (var page in document.Pages) CountForms(page.Resources);
        while (pending.TryPop(out var resources)) CountForms(resources);

        return counts.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToHashSet();
    }

    public static HashSet<FormObjectRange> FindAmbiguous(
        IEnumerable<ContentInterpreter.PageObject> objects, IReadOnlySet<int> sharedForms)
    {
        var ranges = objects.Where(obj => obj.Form != 0).GroupBy(obj => new FormObjectRange(obj.Form, obj.Start, obj.End));
        return ranges.Where(group => sharedForms.Contains(group.Key.Form) || group.Count() > 1)
            .Select(group => group.Key).ToHashSet();
    }

    public static bool IsEditable(ContentInterpreter.PageObject obj, IReadOnlySet<FormObjectRange> ambiguous) =>
        obj.Form == 0 || !ambiguous.Contains(new FormObjectRange(obj.Form, obj.Start, obj.End));
}
