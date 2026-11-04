using PortableDocument.Core;

namespace FreeDF.Tests;

public class StrictPredictorTests
{
    [Theory]
    [InlineData(2, new byte[] { 1, 1, 1, 1 }, new byte[] { 1, 2, 3, 4 })]
    [InlineData(10, new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 1, 2, 3, 4 })]
    [InlineData(11, new byte[] { 1, 1, 1, 1, 1 }, new byte[] { 1, 2, 3, 4 })]
    [InlineData(12, new byte[] { 2, 1, 2, 3, 4, 2, 1, 1, 1, 1 }, new byte[] { 1, 2, 3, 4, 2, 3, 4, 5 })]
    [InlineData(13, new byte[] { 3, 1, 2, 2, 3 }, new byte[] { 1, 2, 3, 4 })]
    [InlineData(14, new byte[] { 4, 1, 1, 1, 1 }, new byte[] { 1, 2, 3, 4 })]
    [InlineData(15, new byte[] { 1, 1, 1, 1, 1, 2, 1, 1, 1, 1 }, new byte[] { 1, 2, 3, 4, 2, 3, 4, 5 })]
    [InlineData(10, new byte[] { 1, 1, 1, 1, 1 }, new byte[] { 1, 2, 3, 4 })]
    public void CompleteFramesReuseExistingPredictorSemantics(int predictor, byte[] raw, byte[] expected) =>
        Assert.Equal(expected, Decode(raw, Parameters(predictor)));

    [Theory]
    [InlineData(2, new byte[] { 1 })]
    [InlineData(2, new byte[] { 1, 1, 1, 1, 1 })]
    [InlineData(10, new byte[] { 0 })]
    [InlineData(11, new byte[] { 1, 1 })]
    [InlineData(15, new byte[] { 0, 1, 1, 1, 1, 0 })]
    [InlineData(15, new byte[] { 5, 1, 1, 1, 1 })]
    [InlineData(15, new byte[] { 255, 1, 1, 1, 1 })]
    public void PartialRowsAndUnknownTagsDoNotProduceCompleteContent(int predictor, byte[] raw) =>
        Assert.Throws<InvalidDataException>(() => Decode(raw, Parameters(predictor)));

    [Theory]
    [InlineData("Predictor", 0)]
    [InlineData("Predictor", 3)]
    [InlineData("Predictor", 9)]
    [InlineData("Predictor", 16)]
    [InlineData("Colors", 0)]
    [InlineData("Colors", -1)]
    [InlineData("BitsPerComponent", 3)]
    [InlineData("Columns", 0)]
    [InlineData("Columns", int.MaxValue)]
    public void InvalidIntegerParametersDoNotFallBack(string key, int value)
    {
        var parameters = Parameters(15);
        parameters.Entries[key] = new PdfInteger(value);
        Assert.Throws<InvalidDataException>(() => Decode([], parameters));
    }

    [Theory]
    [InlineData("Predictor")]
    [InlineData("Colors")]
    [InlineData("BitsPerComponent")]
    [InlineData("Columns")]
    public void NonIntegerParametersDoNotFallBack(string key)
    {
        var parameters = Parameters(15);
        parameters.Entries[key] = new PdfName("Invalid");
        Assert.Throws<InvalidDataException>(() => Decode([], parameters));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(15)]
    public void EmptyCompleteFrameSequenceIsAllowed(int predictor) =>
        Assert.Empty(Decode([], Parameters(predictor)));

    [Fact]
    public void OrdinaryReaderRetainsPartialRowRecovery()
    {
        var result = PdfFilters.Decode(PdfFilters.DeflateEncode([1, 1, 1]),
            [("FlateDecode", Parameters(11))], x => x);
        Assert.Equal(new byte[] { 1, 2, 2, 2 }, result.Data);
    }

    static PdfDictionary Parameters(int predictor) => new()
    {
        Entries = { ["Predictor"] = new PdfInteger(predictor), ["Columns"] = new PdfInteger(4) },
    };

    static byte[] Decode(byte[] raw, PdfDictionary parameters) =>
        PdfFilters.Decode(raw.Length == 0 ? [0x78, 0x9c, 3, 0, 0, 0, 0, 1] : PdfFilters.DeflateEncode(raw),
            [("FlateDecode", parameters)], x => x,
            strictContentFilters: true).Data;
}
