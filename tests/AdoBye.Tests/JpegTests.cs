using PortableDocument.Content;

namespace AdoBye.Tests;

public class JpegTests
{
    static string Fx(string n) => Path.Combine(AppContext.BaseDirectory, "fixtures", "jpeg", n);

    [Theory]
    [InlineData("baseline", 3)]
    [InlineData("progressive", 3)]
    [InlineData("gray-progressive", 1)]
    public void MatchesLibjpeg(string name, int comps)
    {
        var px = JpegDecoder.Decode(File.ReadAllBytes(Fx(name + ".jpg")), out var w, out var h, out var n);
        Assert.NotNull(px);
        Assert.Equal((67, 45, comps), (w, h, n));
        var exp = File.ReadAllBytes(Fx(name + ".raw"));
        Assert.Equal(exp.Length, px!.Length);
        double sum = 0; var max = 0;
        for (var i = 0; i < exp.Length; i++) { var dd = Math.Abs(exp[i] - px[i]); sum += dd; max = Math.Max(max, dd); }
        // 4:2:0 fixtures differ from libjpeg's "fancy" chroma upsampling at colour edges; nearest-neighbour is used here.
        Assert.True(sum / exp.Length < (comps == 1 ? 1 : 6), $"mean abs diff {sum / exp.Length}, max {max}");
    }
}
