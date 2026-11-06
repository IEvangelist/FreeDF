using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using PortableDocument.Core;

[MemoryDiagnoser]
public class IndirectObjectBench
{
    [Params(64, 4096)]
    public int Objects { get; set; }

    [Params(false, true)]
    public bool DictionaryValues { get; set; }

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
            text.Append($"{number} 0 obj\n{(DictionaryValues ? $"<< /Value {number} >>" : $"{number}")}\nendobj\n");
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
            if (DictionaryValues && value is PdfDictionary dictionary) value = dictionary.Get("Value")!;
            if (value is not PdfInteger integer || integer.Value != number)
                throw new InvalidOperationException("Indirect object benchmark value preflight failed.");
        }
        if (file.Repaired || file.Xref.Count != Objects + 2 || LoadValues() != (long)Objects * (Objects + 3) / 2)
            throw new InvalidOperationException("Indirect object benchmark load preflight failed.");
        Console.WriteLine($"PASS indirect objects: count={Objects}, dictionaries={DictionaryValues}, bytes={_pdf.Length}, " +
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
            if (DictionaryValues) value = ((PdfDictionary)value).Get("Value")!;
            sum += ((PdfInteger)value).Value;
        }
        return sum;
    }
}
