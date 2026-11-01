using PortableDocument.Content;
using Xunit;

namespace FreeDF.Tests;

// Restart-interval resynchronisation: damage inside one restart interval must not disturb other intervals.
// References are libjpeg-turbo decodes of the same damaged files (tools/make_jpeg_restart_fixtures.py).
public class JpegRestartRecoveryTests
{
    static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures", "jpeg-restart");

    public static IEnumerable<object[]> Cases() =>
        Directory.GetFiles(Dir, "*.jpg").Order().Select(p => new object[] { Path.GetFileNameWithoutExtension(p) });

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesLibjpegOutsideTheDamagedInterval(string name)
    {
        var meta = File.ReadAllText(Path.Combine(Dir, name + ".meta")).Split(' ').Select(int.Parse).ToArray();
        int w = meta[0], h = meta[1], perRow = meta[2], badStart = meta[3], badEnd = meta[4];
        var refPx = File.ReadAllBytes(Path.Combine(Dir, name + ".ref"));
        var got = JpegDecoder.Decode(File.ReadAllBytes(Path.Combine(Dir, name + ".jpg")), out var gw, out var gh, out var nc);
        Assert.NotNull(got);
        Assert.Equal((w, h, 1), (gw, gh, nc));
        var bad = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var mcu = y / 8 * perRow + x / 8;
                if (mcu >= badStart && mcu < badEnd) continue;
                if (got![y * w + x] != refPx[y * w + x]) bad++;
            }
        Assert.Equal(0, bad);
    }
}
