static class MergeErrorClassifier
{
    static bool ExpectedError(string error) =>
        error.Contains("File 2: no pages in range. The PDF has 1 page(s).", StringComparison.Ordinal)
        || error.Contains("20-report.pdf: no pages in range. The PDF has 6 page(s).", StringComparison.Ordinal);

    public static bool ExpectedErrors(IEnumerable<string> errors, Func<string, bool> otherExpected)
    {
        // JS console errors can arrive between individual WASM stack frames.
        var entries = errors.Where(error =>
            !(error.StartsWith("Error:", StringComparison.Ordinal) && ExpectedError(error))).ToArray();
        string[][] stack =
        [
            ["   at EditorSession.HandleCore(String method, String path, IReadOnlyDictionary`2 query, Byte[] body, String uploadName)"],
            ["   at EditorSession.Handle(String method, String path, IReadOnlyDictionary`2 query, Byte[] body, String uploadName)"],
            ["   at BrowserEditor.Execute(Command c, Byte[] body)",
             "   at BrowserEditor.RequestBytes(String method, String path, String query, String body, JSObject exchange, String fileName)"],
        ];
        for (var i = 0; i < entries.Length; i++)
        {
            if (!ExpectedError(entries[i])) { if (!otherExpected(entries[i])) return false; continue; }
            if (!entries[i].StartsWith("System.ArgumentException:", StringComparison.Ordinal)) continue;
            // AOT may inline ImportRange; the state-boundary frames must still match.
            if (i + 1 < entries.Length && entries[i + 1] == "   at EditorSession.ImportRange(String range, Int32 count, String label)") i++;
            foreach (var frames in stack)
            {
                if (++i >= entries.Length) return false;
                // The permission guard wrapper may be inlined by AOT.
                if (frames.Any(frame => frame.Contains("EditorSession.Handle(")) && entries[i] == "   at EditorSession.HandleGuarded(String method, String path, IReadOnlyDictionary`2 query, Byte[] body, String uploadName)") i++;
                if (i >= entries.Length || !frames.Contains(entries[i])) return false;
            }
        }
        return true;
    }
}
