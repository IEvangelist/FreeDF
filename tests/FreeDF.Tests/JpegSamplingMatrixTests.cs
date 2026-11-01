using PortableDocument.Content;
using Xunit;

namespace FreeDF.Tests;

// Bit-exact comparison with libjpeg-turbo (non-fancy upsampling) across sampling layouts: tools/make_jpeg_sampling_fixtures.py.
public class JpegSamplingMatrixTests
{
    static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures", "jpeg-sampling");

    public static IEnumerable<object[]> Cases() =>
        Directory.GetFiles(Dir, "*.jpg").Order().Select(p => new object[] { Path.GetFileNameWithoutExtension(p) });

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesLibjpegTurbo(string name)
    {
        var meta = File.ReadAllText(Path.Combine(Dir, name + ".meta")).Split(' ').Select(int.Parse).ToArray();
        var expected = File.ReadAllBytes(Path.Combine(Dir, name + ".ref"));
        var got = JpegDecoder.Decode(File.ReadAllBytes(Path.Combine(Dir, name + ".jpg")), out var w, out var h, out var nc);
        Assert.NotNull(got);
        Assert.Equal((meta[0], meta[1], meta[2]), (w, h, nc));
        var max = 0;
        for (var i = 0; i < expected.Length; i++) max = Math.Max(max, Math.Abs(got![i] - expected[i]));
        Assert.True(max <= 0, $"{name}: max sample difference {max}");
    }
}
