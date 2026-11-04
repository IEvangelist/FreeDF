using System.Buffers.Binary;
using System.Text;

namespace PortableDocument.Core;

/// <summary>Reads CFF 1 Type1C glyph-name metadata without interpreting charstrings.</summary>
internal static class CffGlyphNames
{
    public static string[] Read(byte[] data)
    {
        InvalidDataException Invalid() => new("truncated, malformed or unsupported Type1C glyph-name metadata");
        void Require(int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > data.Length - length) throw Invalid();
        }
        int UInt16(int offset)
        {
            Require(offset, 2);
            return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
        }
        (int Start, int End)[] Index(ref int pos)
        {
            var count = UInt16(pos);
            pos += 2;
            if (count == 0) return [];
            Require(pos, 1);
            var size = data[pos++];
            if (size is < 1 or > 4) throw Invalid();
            var offsets = pos;
            Require(offsets, (count + 1) * size);
            var start = offsets + (count + 1) * size;
            int Offset(int index)
            {
                uint value = 0;
                for (var j = 0; j < size; j++) value = value << 8 | data[offsets + index * size + j];
                if (value == 0 || value > (uint)(data.Length - start + 1)) throw Invalid();
                return (int)value - 1;
            }
            if (Offset(0) != 0) throw Invalid();
            var entries = new (int, int)[count];
            var previous = 0;
            for (var i = 0; i < count; i++)
            {
                var next = Offset(i + 1);
                if (next < previous) throw Invalid();
                entries[i] = (start + previous, start + next);
                previous = next;
            }
            pos = start + previous;
            return entries;
        }

        Require(0, 4);
        if (data.Length > 16_777_216 || data[0] != 1 || data[1] != 0 || data[2] < 4 || data[3] is < 1 or > 4)
            throw Invalid();
        var pos = (int)data[2];
        var names = Index(ref pos);
        var tops = Index(ref pos);
        var strings = Index(ref pos);
        _ = Index(ref pos);
        var metadataEnd = pos;
        if (names.Length != 1 || names[0].Start == names[0].End || tops.Length != 1) throw Invalid();
        var operands = new List<int?>();
        var charset = 0;
        var charsetSeen = false;
        var charstrings = -1;
        pos = tops[0].Start;
        var topEnd = tops[0].End;
        void TopBytes(int count)
        {
            if (pos > topEnd - count) throw Invalid();
        }
        while (pos < topEnd)
        {
            var b = data[pos++];
            int? number = null;
            if (b >= 32 && b <= 246) number = b - 139;
            else if (b is >= 247 and <= 254)
            {
                TopBytes(1);
                var next = data[pos++];
                number = b <= 250 ? (b - 247) * 256 + next + 108 : -(b - 251) * 256 - next - 108;
            }
            else if (b == 28)
            {
                TopBytes(2);
                number = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(pos));
                pos += 2;
            }
            else if (b == 29)
            {
                TopBytes(4);
                number = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
                pos += 4;
            }
            else if (b == 30)
            {
                var ended = false;
                while (!ended)
                {
                    TopBytes(1);
                    var value = data[pos++];
                    for (var shift = 4; shift >= 0; shift -= 4)
                    {
                        var nibble = value >> shift & 15;
                        if (nibble == 15) { ended = true; break; }
                        if (nibble == 13) throw Invalid();
                    }
                }
            }
            else
            {
                if (b > 21) throw Invalid();
                var op = (int)b;
                if (b == 12) { TopBytes(1); op = 1200 + data[pos++]; }
                if (op == 1230) throw new InvalidDataException("CID-keyed CFF is not a Type1C glyph-name program");
                if (op is 15 or 17)
                {
                    if (operands.Count != 1 || operands[0] is not { } offset || offset < 0) throw Invalid();
                    if (op == 15)
                    {
                        if (charsetSeen) throw Invalid();
                        charset = offset;
                        charsetSeen = true;
                    }
                    else
                    {
                        if (charstrings >= 0) throw Invalid();
                        charstrings = offset;
                    }
                }
                operands.Clear();
                continue;
            }
            if (operands.Count >= 48) throw Invalid();
            operands.Add(number);
        }
        if (operands.Count != 0 || charstrings < metadataEnd) throw Invalid();
        pos = charstrings;
        var glyphs = Index(ref pos);
        if (glyphs.Length == 0) throw Invalid();
        var sids = new int[glyphs.Length];
        if (charset <= 2)
        {
            var predefined = charset switch
            {
                0 => CffStandardStrings.ISOAdobeCharset,
                1 => CffStandardStrings.ExpertCharset,
                _ => CffStandardStrings.ExpertSubsetCharset,
            };
            if (sids.Length > predefined.Length) throw Invalid();
            Array.Copy(predefined, sids, sids.Length);
        }
        else
        {
            if (charset < metadataEnd) throw Invalid();
            Require(charset, 1);
            var format = data[charset++];
            if (format > 2) throw Invalid();
            for (var gid = 1; gid < sids.Length;)
            {
                var sid = UInt16(charset);
                charset += 2;
                var count = 1;
                if (format == 1) { Require(charset, 1); count += data[charset++]; }
                else if (format == 2) { count += UInt16(charset); charset += 2; }
                if (count > sids.Length - gid || sid + count - 1 > 65_535) throw Invalid();
                for (var j = 0; j < count; j++) sids[gid++] = sid + j;
            }
        }
        var custom = strings.Select(s => Encoding.Latin1.GetString(data, s.Start, s.End - s.Start)).ToArray();
        var result = new string[sids.Length];
        for (var gid = 0; gid < result.Length; gid++)
        {
            var sid = sids[gid];
            if (sid < CffStandardStrings.Strings.Length) result[gid] = CffStandardStrings.Strings[sid];
            else if (sid - CffStandardStrings.Strings.Length < custom.Length) result[gid] = custom[sid - CffStandardStrings.Strings.Length];
            else throw Invalid();
        }
        return result;
    }
}
