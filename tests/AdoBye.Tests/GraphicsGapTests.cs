using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace AdoBye.Tests;

public class GraphicsGapTests
{
    static (int R, int G, int B) Px(RenderedPage p, int x, int y) { var o = (y * p.Width + x) * 4; return (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]); }

    static RenderedPage Render(string content, string res, string objs = "", Func<string, string>? patch = null)
    {
        var bytes = TestPdf.Build(content, res, objs, brokenXref: true);
        if (patch is not null) bytes = System.Text.Encoding.Latin1.GetBytes(patch(System.Text.Encoding.Latin1.GetString(bytes)));
        return DocumentRenderer.RenderPage(PdfDocument.Load(bytes).Pages[0], 72);
    }

    static void Near((int R, int G, int B) a, (int, int, int) e, int tol = 4)
        => Assert.True(Math.Abs(a.R - e.Item1) <= tol && Math.Abs(a.G - e.Item2) <= tol && Math.Abs(a.B - e.Item3) <= tol, $"expected {e} got {a}");

    const string D65 = "[0.9505 1 1.089]";

    static (int, int, int) Fill(string cs, string color) => Px(Render($"/C cs {color} sc 0 0 200 200 re f", $"/ColorSpace << /C {cs} >>"), 100, 100);

    [Fact]
    public void CalGray_AppliesGamma()
    {
        Near(Fill($"[/CalGray << /WhitePoint {D65} /Gamma 1 >>]", "0.5"), (188, 188, 188));
        Near(Fill($"[/CalGray << /WhitePoint {D65} /Gamma 2.2 >>]", "0.5"), (128, 128, 128));
    }

    [Fact]
    public void CalRgb_GammaAndMatrix()
    {
        const string srgbM = "[0.4124 0.2126 0.0193 0.3576 0.7152 0.1192 0.1805 0.0722 0.9505]";
        Near(Fill($"[/CalRGB << /WhitePoint {D65} /Gamma [2.2 2.2 2.2] /Matrix {srgbM} >>]", "0.5 0.5 0.5"), (128, 128, 128));
        Near(Fill($"[/CalRGB << /WhitePoint {D65} /Gamma [2.2 2.2 2.2] /Matrix {srgbM} >>]", "1 0 0"), (255, 0, 0));
        Near(Fill($"[/CalRGB << /WhitePoint {D65} /Gamma [1 1 1] /Matrix {srgbM} >>]", "0.5 0.5 0.5"), (188, 188, 188));
    }

    [Fact]
    public void CalRgb_WhitePointIsMappedToWhite_AndBadDictFallsBack()
    {
        Near(Fill("[/CalRGB << /WhitePoint [0.9642 1 0.8249] /Gamma [1.8 1.8 1.8] /Matrix [0.4361 0.2225 0.0139 0.3851 0.7169 0.0971 0.1431 0.0606 0.7141] >>]", "1 1 1"), (255, 255, 255), 2);
        Near(Fill("[/CalRGB << /WhitePoint [0 0 0] >>]", "1 0 0"), (255, 0, 0), 0);
        Near(Fill("[/CalRGB]", "0 1 0"), (0, 255, 0), 0);
    }

    [Fact]
    public void Lab_UsesWhitePointAndRange()
    {
        const string d50 = "[0.9642 1 0.8249]";
        Near(Fill($"[/Lab << /WhitePoint {d50} /Range [-128 127 -128 127] >>]", "100 0 0"), (255, 255, 255), 2);
        Near(Fill($"[/Lab << /WhitePoint {d50} /Range [-128 127 -128 127] >>]", "50 0 0"), (119, 119, 119));
        Near(Fill($"[/Lab << /WhitePoint {d50} /Range [-128 127 -128 127] >>]", "54.29 80.80 69.89"), (255, 0, 0), 6);
        Near(Fill($"[/Lab << /WhitePoint {D65} /Range [-128 127 -128 127] >>]", "53.24 80.09 67.2"), (255, 0, 0), 6);
        Near(Fill($"[/Lab << /WhitePoint {D65} /Range [-10 10 -10 10] >>]", "50 100 0"), Fill($"[/Lab << /WhitePoint {D65} /Range [-10 10 -10 10] >>]", "50 10 0"), 0);
    }

    const string TwoSquares = "/A gs 1 0 0 rg 0 0 120 200 re f 0 0 1 rg 80 0 120 200 re f";

    [Fact]
    public void PageGroupKnockout_TopLevelObjectsKnockOut()
    {
        const string res = "/ExtGState << /A << /ca 0.5 >> >>";
        var ko = Render(TwoSquares, res, patch: s => s.Replace("/Type /Page /Parent", "/Type /Page /Group << /S /Transparency /CS /DeviceRGB /K true >> /Parent"));
        Near(Px(ko, 100, 100), (127, 127, 255));
        Near(Px(ko, 20, 100), (255, 127, 127));
        var plain = Render(TwoSquares, res, patch: s => s.Replace("/Type /Page /Parent", "/Type /Page /Group << /S /Transparency /CS /DeviceRGB /I true >> /Parent"));
        Near(Px(plain, 100, 100), (127, 63, 191));
    }

    static string Mask() => "/ExtGState << /G1 << /SMask << /Type /Mask /S /Luminosity /G 5 0 R >> >> /G0 << /SMask /None >> >>";
    const string MaskForm = "5 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /CS /DeviceGray >> /Length 28 >>\nstream\n1 g 0 0 200 200 re f      \nendstream\nendobj\n";

    [Fact]
    public void ClipSetWhileMaskActive_SurvivesClearingTheMask()
    {
        var rect = Render("q /G1 gs 0 0 50 200 re W n /G0 gs 1 0 0 rg 0 0 200 200 re f Q", Mask(), MaskForm);
        Near(Px(rect, 25, 100), (255, 0, 0), 0);
        Near(Px(rect, 150, 100), (255, 255, 255), 0);
        var tri = Render("q /G1 gs 0 0 m 100 0 l 0 200 l h W n /G0 gs 1 0 0 rg 0 0 200 200 re f Q", Mask(), MaskForm);
        Near(Px(tri, 10, 150), (255, 0, 0), 0);
        Near(Px(tri, 150, 150), (255, 255, 255), 0);
        // and the mask itself still applies while active, then restores after Q
        var after = Render("q /G1 gs 0 0 50 200 re W n Q 0 0 1 rg 150 0 50 50 re f", Mask(), MaskForm);
        Near(Px(after, 175, 175), (0, 0, 255), 0);
    }

    static string MeshObjects(int type, string function, string colorSpace = "/DeviceRGB", bool packed = false)
    {
        var data = new List<byte>();
        var bits = new List<int>();
        void Write(int value, int count)
        {
            for (var i = count - 1; i >= 0; i--) bits.Add((value >> i) & 1);
        }
        void Align()
        {
            while (bits.Count % 8 != 0) bits.Add(0);
        }
        void Point(int x, int y) { Write(x, 8); Write(y, 8); }
        void Component(int value)
        {
            Write(value, packed ? 4 : 8);
            if (function.Length == 0) { Write(0, packed ? 4 : 8); Write(0, packed ? 4 : 8); }
        }
        void Vertex(int x, int y, int value, int flag = 0)
        {
            if (type == 4) Write(flag, packed ? 2 : 8);
            Point(x, y); Component(value); Align();
        }
        var max = packed ? 15 : 255;
        if (type is 4 or 5)
        {
            Vertex(0, 0, 0); Vertex(255, 0, max); Vertex(0, 255, 0);
            Vertex(255, 255, max, 1);
        }
        else
        {
            Write(0, packed ? 2 : 8);
            (int X, int Y)[] points = [(0, 0), (85, 0), (170, 0), (255, 0),
                (255, 85), (255, 170), (255, 255), (170, 255), (85, 255), (0, 255), (0, 170), (0, 85),
                (85, 85), (170, 85), (170, 170), (85, 170)];
            foreach (var (x, y) in points.Take(type == 6 ? 12 : 16)) Point(x, y);
            Component(0); Component(max); Component(max); Component(0); Align();
        }
        for (var i = 0; i < bits.Count; i += 8)
        {
            var value = 0;
            for (var j = 0; j < 8; j++) value = (value << 1) | bits[i + j];
            data.Add((byte)value);
        }
        var stream = System.Text.Encoding.Latin1.GetString(data.ToArray());
        var decode = function.Length == 0 ? "0 1 0 1 0 1" : "0 1";
        return $"5 0 obj\n<< /ShadingType {type} /ColorSpace {colorSpace} /BitsPerCoordinate 8 /BitsPerComponent {(packed ? 4 : 8)} " +
            $"/BitsPerFlag {(packed ? 2 : 8)} /VerticesPerRow 2 /Decode [0 200 0 200 {decode}] {function} /Length {stream.Length} >>\nstream\n{stream}\nendstream\nendobj\n";
    }

    const string SquareFunction = "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 0 0] /N 2 >>";
    const string SquareComponent = "<< /FunctionType 2 /Domain [0 1] /N 2 >>";
    const string CubeComponent = "<< /FunctionType 2 /Domain [0 1] /N 3 >>";
    const string ZeroComponent = "<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [0] /N 1 >>";

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    public void MeshFunction_EvaluatesInterpolatedInputAtEachPixel(int type, bool packed)
    {
        var p = Render("/M sh", "/Shading << /M 5 0 R >>", MeshObjects(type, $"/Function {SquareFunction}", packed: packed));
        Assert.Empty(p.Warnings);
        foreach (var x in new[] { 25, 50, 100, 150, 175 })
        {
            var t = (x + 0.5) / 200;
            Near(Px(p, x, 100), ((int)Math.Round(255 * t * t), 0, 0), 1);
        }
        // Vertex-colour interpolation would produce about 128, not 64.
        Assert.InRange(Px(p, 100, 100).R, 63, 65);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void MeshFunction_IsNotEvaluatedAtTessellationVertices(int type)
    {
        var function = SquareFunction.Replace("/N 2", "/N 100");
        var p = Render("/M sh", "/Shading << /M 5 0 R >>", MeshObjects(type, $"/Function {function}"));
        Assert.Empty(p.Warnings);
        foreach (var x in new[] { 193, 195, 197 })
        {
            var t = (x + 0.5) / 200;
            Near(Px(p, x, 100), ((int)Math.Round(255 * Math.Pow(t, 100)), 0, 0), 1);
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void MeshFunctionArray_PreservesSingleOutputFunctionsInComponentOrder(int type)
    {
        var function = $"/Function [6 0 R {CubeComponent} {ZeroComponent}]";
        var objects = MeshObjects(type, function) + $"6 0 obj\n{SquareComponent}\nendobj\n";
        var p = Render("/M sh", "/Shading << /M 5 0 R >>", objects);
        Assert.Empty(p.Warnings);
        foreach (var x in new[] { 50, 100, 150 })
        {
            var t = (x + 0.5) / 200;
            Near(Px(p, x, 100), ((int)Math.Round(255 * t * t), (int)Math.Round(255 * t * t * t), 0), 1);
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void MeshGrayFunction_AcceptsSingleFunctionAndSingleElementArray(int type)
    {
        foreach (var function in new[] { $"/Function {SquareComponent}", $"/Function [{SquareComponent}]" })
        {
            var p = Render("/M sh", "/Shading << /M 5 0 R >>", MeshObjects(type, function, "/DeviceGray"));
            Assert.Empty(p.Warnings);
            Near(Px(p, 100, 100), (64, 64, 64), 1);
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void MeshWithoutFunction_PreservesComponentInterpolation(int type)
    {
        var p = Render("/M sh", "/Shading << /M 5 0 R >>", MeshObjects(type, ""));
        Assert.Empty(p.Warnings);
        foreach (var x in new[] { 25, 100, 175 })
            Near(Px(p, x, 100), ((int)Math.Round(255 * (x + 0.5) / 200), 0, 0), 1);
    }

    [Theory]
    [InlineData("/Function << /FunctionType 99 >>")]
    [InlineData("/Function /Unsupported")]
    [InlineData("/Function [<< /FunctionType 99 >>]")]
    [InlineData("/Function [null]")]
    [InlineData("/Function []")]
    [InlineData("/Function [<< /FunctionType 2 /Domain [0 1] /N 2 >> null << /FunctionType 2 /Domain [0 1] /N 2 >>]")]
    [InlineData("/Function [<< /FunctionType 2 /Domain [0 1] /N 2 >> << /FunctionType 99 >> << /FunctionType 2 /Domain [0 1] /N 2 >>]")]
    [InlineData("/Function [<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 0 0] /N 2 >> << /FunctionType 2 /Domain [0 1] /N 2 >> << /FunctionType 2 /Domain [0 1] /N 2 >>]")]
    [InlineData("/Function << /FunctionType 2 /Domain [0 1] /N 2 >>")]
    [InlineData("/Function << /FunctionType 2 /Domain [0 1 0 1] /C0 [0 0 0] /C1 [1 0 0] /N 2 >>")]
    public void MeshInvalidFunction_IsDiagnosedRatherThanDropped(string function)
    {
        var p = Render("/M sh 0 1 0 rg 0 0 10 10 re f", "/Shading << /M 5 0 R >>", MeshObjects(4, function));
        Assert.Contains(p.Warnings, warning => warning.Contains("mesh shading Function", StringComparison.Ordinal));
        Near(Px(p, 100, 100), (255, 255, 255), 0);
        Near(Px(p, 5, 195), (0, 255, 0), 0);
    }
}
