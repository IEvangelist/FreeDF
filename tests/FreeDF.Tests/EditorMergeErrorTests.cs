namespace FreeDF.Tests;

public class EditorMergeErrorTests
{
    static readonly string[] Stack =
    [
        "System.ArgumentException: 20-report.pdf: no pages in range. The PDF has 6 page(s).",
        "   at EditorSession.HandleCore(String method, String path, IReadOnlyDictionary`2 query, Byte[] body, String uploadName)",
        "   at EditorSession.HandleGuarded(String method, String path, IReadOnlyDictionary`2 query, Byte[] body, String uploadName)",
        "   at EditorSession.Handle(String method, String path, IReadOnlyDictionary`2 query, Byte[] body, String uploadName)",
        "   at BrowserEditor.RequestBytes(String method, String path, String query, String body, JSObject exchange, String fileName)",
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ExpectedConsoleErrorCanInterleaveStackFrames(int position)
    {
        var entries = Stack.ToList();
        entries.Insert(position, "Error: 20-report.pdf: no pages in range. The PDF has 6 page(s).\n    at V.ne.onmessage (http://localhost/host.js:1:1713)");
        Assert.True(MergeErrorClassifier.ExpectedErrors(entries, _ => false));
    }

    [Fact]
    public void MissingBoundaryFrameStillFails() =>
        Assert.False(MergeErrorClassifier.ExpectedErrors(Stack.Where((_, i) => i != 3), _ => false));

    [Fact]
    public void UnexpectedInterleavedErrorStillFails()
    {
        var entries = Stack.ToList();
        entries.Insert(2, "Error: unexpected engine failure");
        Assert.False(MergeErrorClassifier.ExpectedErrors(entries, _ => false));
    }
}
