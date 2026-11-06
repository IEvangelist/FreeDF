using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using PortableDocument.Core;

[MemoryDiagnoser]
public class NestedObjectBench
{
    [Params(64, 4096)]
    public int Objects { get; set; }

    [Params(false, true)]
    public bool StreamValues { get; set; }

    byte[] _pdf = [];

    [GlobalSetup]
    public void Setup()
    {
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int> { text.Length };
        text.Append("1 0 obj\n<< /Type /Catalog >>\nendobj\n");
        for (var number = 2; number < Objects + 2; number++)
        {
            offsets.Add(text.Length);
            text.Append($"{number} 0 obj\n<< /Value [<< /Number {number} /Names [/q /BT] >> true null (endobj)]");
            text.Append(StreamValues ? " /Length 8 >>\nstream\nq BT Q x\nendstream\nendobj\n" : " >>\nendobj\n");
        }
        var xref = text.Length;
        text.Append($"xref\n0 {Objects + 2}\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append($"{offset:D10} 00000 n \n");
        text.Append($"trailer\n<< /Size {Objects + 2} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        _pdf = Encoding.ASCII.GetBytes(text.ToString());
        var file = PdfFile.Load(_pdf);
        for (var number = 2; number < Objects + 2; number++)
        {
            var value = file.GetObject(number);
            if (StreamValues)
            {
                if (value is not PdfStream stream || !stream.RawData.AsSpan().SequenceEqual("q BT Q x"u8))
                    throw new InvalidOperationException("Nested object benchmark stream preflight failed.");
                value = stream.Dict;
            }
            if (value is not PdfDictionary dictionary || dictionary.Get("Value") is not PdfArray { Items.Count: 4 } array
                || array.Items[0] is not PdfDictionary inner || inner.Get("Number") is not PdfInteger integer
                || integer.Value != number || inner.Get("Names") is not PdfArray { Items.Count: 2 } names
                || names.Items[0] is not PdfName { Value: "q" } || names.Items[1] is not PdfName { Value: "BT" }
                || array.Items[1] is not PdfBoolean { Value: true } || array.Items[2] is not PdfNull
                || array.Items[3] is not PdfString { Text: "endobj" })
                throw new InvalidOperationException("Nested object benchmark value preflight failed.");
        }
        if (file.Repaired || file.Xref.Count != Objects + 2 || LoadValues() != (long)Objects * (Objects + 3) / 2)
            throw new InvalidOperationException("Nested object benchmark load preflight failed.");
        Console.WriteLine($"PASS nested objects: count={Objects}, streams={StreamValues}, bytes={_pdf.Length}, " +
            $"SHA256={Convert.ToHexString(SHA256.HashData(_pdf))}, engine={typeof(PdfFile).Module.ModuleVersionId}.");
    }

    [Benchmark]
    public long LoadValues()
    {
        var file = PdfFile.Load(_pdf);
        long sum = 0;
        for (var number = 2; number < Objects + 2; number++)
        {
            var value = file.GetObject(number);
            var dictionary = StreamValues ? ((PdfStream)value).Dict : (PdfDictionary)value;
            var array = (PdfArray)dictionary.Get("Value")!;
            sum += ((PdfInteger)((PdfDictionary)array.Items[0]).Get("Number")!).Value;
        }
        return sum;
    }
}
