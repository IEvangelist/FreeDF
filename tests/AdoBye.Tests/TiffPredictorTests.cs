using PortableDocument.Core;

namespace AdoBye.Tests;

/// <summary>ISO 32000-2 7.4.4.4: the TIFF predictor (Predictor 2) applies to every legal BitsPerComponent, not only 8.</summary>
public class TiffPredictorTests
{
    static byte[] Decode(byte[] encoded, int colors, int bpc, int columns)
    {
        var parms = new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Predictor"] = new PdfInteger(2), ["Colors"] = new PdfInteger(colors),
            ["BitsPerComponent"] = new PdfInteger(bpc), ["Columns"] = new PdfInteger(columns),
        });
        var raw = PdfFilters.DeflateEncode(encoded);
        return PdfFilters.Decode(raw, [("FlateDecode", parms)], o => o).Data;
    }

    [Fact]
    public void Predictor2_8BitRgb() =>
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30, 0x11, 0x22, 0x33 }, Decode([0x10, 0x20, 0x30, 0x01, 0x02, 0x03], 3, 8, 2));

    [Fact]
    public void Predictor2_16BitGrayWrapsModulo65536() =>
        Assert.Equal(new byte[] { 0x01, 0x00, 0x02, 0x00, 0xFF, 0xFF }, Decode([0x01, 0x00, 0x01, 0x00, 0xFD, 0xFF], 1, 16, 3));

    [Fact]
    public void Predictor2_16BitRgbDifferencesPerComponent() =>
        Assert.Equal(new byte[] { 0, 1, 0, 2, 0, 3, 0, 11, 0, 22, 0, 33 }, Decode([0, 1, 0, 2, 0, 3, 0, 10, 0, 20, 0, 30], 3, 16, 2));

    [Fact]
    public void Predictor2_4BitGray() =>
        Assert.Equal(new byte[] { 0x12, 0x31 }, Decode([0x11, 0x1E], 1, 4, 4));

    [Fact]
    public void Predictor2_2BitGrayMasksOverflow() =>
        Assert.Equal(new byte[] { 0b01_10_11_00 }, Decode([0b01_01_01_01], 1, 2, 4));

    [Fact]
    public void Predictor2_1BitRgbUsesComponentOffset() =>
        Assert.Equal(new byte[] { 0xAC }, Decode([0xB8], 3, 1, 2));

    [Fact]
    public void Predictor2_RowsAreIndependentAndPaddingIsPreserved()
    {
        var decoded = Decode([0x11, 0x10, 0x11, 0x10], 1, 4, 3);
        Assert.Equal(new byte[] { 0x12, 0x30, 0x12, 0x30 }, decoded);
    }
}
