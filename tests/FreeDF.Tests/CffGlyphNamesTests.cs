using System.Buffers.Binary;
using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class CffGlyphNamesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CustomCharsetFormatsReadTheSameNames(int format)
    {
        var charset = format switch
        {
            0 => new byte[] { 0, 0, 34, 0, 35, 0, 1 },
            1 => [1, 0, 34, 1, 0, 1, 0],
            _ => [2, 0, 34, 0, 1, 0, 1, 0, 0],
        };
        Assert.Equal(new[] { ".notdef", "A", "B", "space" },
            CffGlyphNames.Read(Build(4, charset)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PredefinedCharsetsUseTheSharedTables(int predefined)
    {
        var expected = predefined switch
        {
            0 => CffStandardStrings.ISOAdobeCharset,
            1 => CffStandardStrings.ExpertCharset,
            _ => CffStandardStrings.ExpertSubsetCharset,
        };
        Assert.Equal(expected.Take(4).Select(sid => CffStandardStrings.Strings[sid]),
            CffGlyphNames.Read(Build(4, null, predefined)));
    }

    [Fact]
    public void CustomStringsAndRealOperandsAreReadWithoutCharstringExecution()
    {
        var data = Build(3, [0, 1, 135, 0, 1], strings: ["customGlyph"],
            prefix: [30, 0xA0, 0x01, 0xFF, 139, 139, 30, 0xA0, 0x01, 0xFF, 139, 139, 12, 7]);
        Assert.Equal(new[] { ".notdef", "customGlyph", "space" }, CffGlyphNames.Read(data));
    }

    [Fact]
    public void EveryTruncationIsRejected()
    {
        var data = Build(4, [0, 0, 34, 0, 35, 0, 1]);
        for (var length = 0; length < data.Length; length++)
            Assert.Throws<InvalidDataException>(() => CffGlyphNames.Read(data[..length]));
    }

    [Fact]
    public void IndexOffsetsAndSizesAreValidated()
    {
        var data = Build(4, [0, 0, 34, 0, 35, 0, 1]);
        foreach (var (index, value) in new (int, byte)[]
        {
            (0, 2), (1, 1), (2, 3), (3, 0), (3, 5),
            (4, 0xFF), (6, 0), (6, 5), (7, 0), (7, 2), (8, 0), (8, 0xFF),
        })
        {
            var malformed = data.ToArray();
            malformed[index] = value;
            Assert.Throws<InvalidDataException>(() => CffGlyphNames.Read(malformed));
        }
    }

    [Fact]
    public void MalformedCharsetsAndUndefinedSidsAreRejected()
    {
        foreach (var charset in new byte[][]
        {
            [3, 0, 34], [0, 0xFF, 0xFF], [1, 0, 34, 255], [2, 0xFF, 0xFF, 0, 1],
        })
            Assert.Throws<InvalidDataException>(() => CffGlyphNames.Read(Build(2, charset)));
        Assert.Throws<InvalidDataException>(() => CffGlyphNames.Read(Build(2, [0, 1, 135])));
        Assert.Throws<InvalidDataException>(() => CffGlyphNames.Read(Build(0, null)));
        Assert.Throws<InvalidDataException>(() => CffGlyphNames.Read(Build(256, null)));
    }

    [Fact]
    public void InvalidTopDictMetadataAndCidProgramsAreRejected()
    {
        foreach (var prefix in new byte[][]
        {
            [139, 139, 139, 12, 30], [139, 15], [139, 17], [30, 0xDF], [255],
            [139, 139, 15], [30, 0x0F, 15],
        })
            Assert.Throws<InvalidDataException>(() => CffGlyphNames.Read(Build(2, [0, 0, 34], prefix: prefix)));
    }

    internal static byte[] Build(int glyphs, byte[]? charset, int predefined = 0, string[]? strings = null, byte[]? prefix = null)
    {
        prefix ??= [];
        var names = Index(["T"u8.ToArray()]);
        var custom = Index((strings ?? []).Select(Encoding.ASCII.GetBytes).ToArray());
        var header = new byte[] { 1, 0, 4, 4 };
        var topLength = Index([new byte[prefix.Length + 12]]).Length;
        var start = header.Length + names.Length + topLength + custom.Length + 2;
        byte[] Integer(int value)
        {
            var bytes = new byte[5];
            bytes[0] = 29;
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(1), value);
            return bytes;
        }
        var top = prefix.Concat(Integer(charset is null ? predefined : start)).Append((byte)15)
            .Concat(Integer(start + (charset?.Length ?? 0))).Append((byte)17).ToArray();
        return header.Concat(names).Concat(Index([top])).Concat(custom).Concat(new byte[] { 0, 0 })
            .Concat(charset ?? []).Concat(Index(Enumerable.Range(0, glyphs).Select(_ => new byte[] { 14 }).ToArray())).ToArray();
    }

    static byte[] Index(byte[][] entries)
    {
        if (entries.Length == 0) return [0, 0];
        var length = entries.Sum(e => e.Length);
        var size = length < 255 ? 1 : 2;
        using var output = new MemoryStream();
        output.WriteByte((byte)(entries.Length >> 8));
        output.WriteByte((byte)entries.Length);
        output.WriteByte((byte)size);
        var offset = 1;
        foreach (var entry in entries.Append([]))
        {
            if (size == 2) output.WriteByte((byte)(offset >> 8));
            output.WriteByte((byte)offset);
            offset += entry.Length;
        }
        foreach (var entry in entries) output.Write(entry);
        return output.ToArray();
    }
}
