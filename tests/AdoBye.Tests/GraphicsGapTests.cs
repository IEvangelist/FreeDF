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

    // ---- Function engine (ISO 32000-2 7.10), observed through an axial shading: pixel x samples t = (x + 0.5) / 200.

    static string AxialFunction(string function, string body, string extend = "/Extend [true true]") =>
        $"5 0 obj\n<< /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 200 0] /Function 6 0 R {extend} >>\nendobj\n" +
        $"6 0 obj\n<< {function} /Length {body.Length} >>\nstream\n{body}\nendstream\nendobj\n";

    static int Gray(RenderedPage p, int x) => Px(p, x, 100).R;

    static int Level(double v) => (int)Math.Round(255 * Math.Clamp(v, 0, 1));

    static RenderedPage Axial(string function, string body) =>
        Render("/S sh", "/Shading << /S 5 0 R >>", AxialFunction(function, body));

    [Theory]
    [InlineData("{ pop 7 2 idiv 3 eq { 1 } { 0 } ifelse }", 255)]
    [InlineData("{ pop 7 2 div 3.5 eq { 1 } { 0 } ifelse }", 255)]
    [InlineData("{ pop 7 -2 mod }", 255)]
    [InlineData("{ pop -7 2 mod -1 eq { 1 } { 0 } ifelse }", 255)]
    [InlineData("{ pop 1 not -2 eq { 1 } { 0 } ifelse }", 255)]
    [InlineData("{ pop true not { 1 } { 0 } ifelse }", 0)]
    [InlineData("{ pop 5 3 and }", 255)]
    [InlineData("{ pop true false or { 1 } { 0 } ifelse }", 255)]
    [InlineData("{ pop 3 1 bitshift 6 eq { 1 } { 0 } ifelse }", 255)]
    [InlineData("{ pop 2.5 round 3 eq { 1 } { 0 } ifelse }", 255)]
    [InlineData("{ pop -2.5 round -2 eq { 1 } { 0 } ifelse }", 255)]
    [InlineData("{ pop 3.9 cvi 3 eq { 1 } { 0 } ifelse }", 255)]
    [InlineData("{ pop 1 1 atan 45 sub abs 0.001 lt { 1 } { 0 } ifelse }", 255)]
    [InlineData("{ pop -1 -1 atan 225 sub abs 0.001 lt { 1 } { 0 } ifelse }", 255)]
    [InlineData("{ pop 0.25 0.5 0.75 3 -1 roll pop pop }", 128)]
    [InlineData("{ pop 0.25 0.5 0.75 3 1 roll pop pop }", 191)]
    [InlineData("{ pop 0.5 0.25 2 copy pop pop pop }", 128)]
    [InlineData("{ pop 0.25 0.75 1 index pop pop }", 64)]
    [InlineData("{ pop 100 log 8 div }", 64)]
    [InlineData("{ pop 4 0.5 exp 8 div }", 64)]
    [InlineData("{ % comment\n pop 0.5 }", 128)]
    public void PostScriptCalculator_FollowsTypedOperatorSemantics(string program, int expected)
    {
        var p = Axial("/FunctionType 4 /Domain [0 1] /Range [0 1]", program);
        Assert.Empty(p.Warnings);
        Assert.InRange(Gray(p, 100), expected - 1, expected + 1);
    }

    [Fact]
    public void PostScriptCalculator_IfAndIfElseBranchOnTheInput()
    {
        var p = Axial("/FunctionType 4 /Domain [0 1] /Range [0 1]", "{ dup 0.5 gt { 1 exch sub } if }");
        Assert.InRange(Gray(p, 50), Level(50.5 / 200) - 1, Level(50.5 / 200) + 1);
        Assert.InRange(Gray(p, 150), Level(1 - 150.5 / 200) - 1, Level(1 - 150.5 / 200) + 1);
        var q = Axial("/FunctionType 4 /Domain [0 1] /Range [0 1]", "{ 0.5 lt { 0 } { 1 } ifelse }");
        Assert.Equal(0, Gray(q, 50));
        Assert.Equal(255, Gray(q, 150));
    }

    [Fact]
    public void SampledFunction_AppliesEncodeAndDecodeInReverse()
    {
        var p = Axial("/FunctionType 0 /Domain [0 1] /Range [0 1] /Size [3] /BitsPerSample 8 /Encode [2 0] /Decode [1 0]", "\0\u0080\u00ff");
        Assert.Empty(p.Warnings);
        foreach (var x in new[] { 10, 50, 100, 150, 190 })
        {
            var t = (x + 0.5) / 200;
            var e = 2 - 2 * t;
            var s = e <= 1 ? e * (128 / 255.0) : 128 / 255.0 + (e - 1) * (127 / 255.0);
            var g = Level(1 - s);
            Assert.InRange(Gray(p, x), g - 1, g + 1);
        }
    }

    [Fact]
    public void SampledFunction_TwoInputsInterpolateMultilinearly()
    {
        // First input varies fastest: samples are f(0,0)=0, f(1,0)=255, f(0,1)=255, f(1,1)=0.
        const string objs = "5 0 obj\n<< /ShadingType 1 /ColorSpace /DeviceGray /Domain [0 1 0 1] /Matrix [200 0 0 200 0 0] /Function 6 0 R >>\nendobj\n" +
            "6 0 obj\n<< /FunctionType 0 /Domain [0 1 0 1] /Range [0 1] /Size [2 2] /BitsPerSample 8 /Length 4 >>\nstream\n\0\u00ff\u00ff\0\nendstream\nendobj\n";
        var p = Render("/S sh", "/Shading << /S 5 0 R >>", objs);
        Assert.Empty(p.Warnings);
        foreach (var (x, y) in new[] { (100, 100), (50, 150), (150, 40), (20, 20) })
        {
            double s = (x + 0.5) / 200, t = (200 - y - 0.5) / 200;
            var g = Level(s * (1 - t) + (1 - s) * t);
            Assert.InRange(Px(p, x, y).R, g - 1, g + 1);
        }
    }

    [Fact]
    public void StitchingFunction_UsesEncodeReversalAndClosedLastSubdomain()
    {
        const string fn = "/FunctionType 3 /Domain [0 1] /Bounds [0.5] /Encode [1 0 0 1] /Functions [<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >>]";
        var p = Render("/S sh", "/Shading << /S 5 0 R >>", "5 0 obj\n<< /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 200 0] /Function << " + fn + " >> /Extend [true true] >>\nendobj\n");
        Assert.Empty(p.Warnings);
        foreach (var x in new[] { 20, 50, 99, 100, 150, 190 })
        {
            var t = (x + 0.5) / 200;
            var g = Level(t < 0.5 ? 1 - t / 0.5 : (t - 0.5) / 0.5);
            Assert.InRange(Gray(p, x), g - 2, g + 2);
        }
    }

    [Fact]
    public void StitchingFunction_ShortBoundsArrayDoesNotThrow()
    {
        const string fn = "/FunctionType 3 /Domain [0 1] /Bounds [] /Encode [0 1 0 1] /Functions [<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> << /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>]";
        var p = Render("/S sh", "/Shading << /S 5 0 R >>", "5 0 obj\n<< /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 200 0] /Function << " + fn + " >> /Extend [true true] >>\nendobj\n");
        Assert.Empty(p.Warnings);
        Assert.InRange(Gray(p, 100), 126, 130);
    }

    [Fact]
    public void AxialShading_HardStopLandsOnTheExactPixelBoundary()
    {
        const string fn = "/FunctionType 3 /Domain [0 1] /Bounds [0.5] /Encode [0 1 0 1] /Functions [<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [0] /N 1 >> << /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [1] /N 1 >>]";
        var p = Render("/S sh", "/Shading << /S 5 0 R >>", "5 0 obj\n<< /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 200 0] /Function << " + fn + " >> >>\nendobj\n");
        Assert.Equal(0, Gray(p, 99));
        Assert.Equal(255, Gray(p, 100));
    }

    // ---- Shading /BBox and /Background (8.7.4.3)

    const string AxialRed = "/ShadingType 2 /ColorSpace /DeviceRGB /Coords [50 0 150 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [1 0 0] /N 1 >>";

    [Fact]
    public void ShadingBBox_ClipsShOperator()
    {
        var p = Render("/S sh", "/Shading << /S 5 0 R >>", $"5 0 obj\n<< {AxialRed} /Extend [true true] /BBox [0 0 100 200] >>\nendobj\n");
        Near(Px(p, 50, 100), (255, 0, 0), 0);
        Near(Px(p, 150, 100), (255, 255, 255), 0);
    }

    [Fact]
    public void ShadingBackground_AppliesToPatternsButNotToSh()
    {
        const string objs = "6 0 obj\n<< /PatternType 2 /Shading 5 0 R >>\nendobj\n";
        var shading = $"5 0 obj\n<< {AxialRed} /Background [0 1 0] >>\nendobj\n";
        var viaPattern = Render("/Pattern cs /P scn 0 0 200 200 re f", "/Pattern << /P 6 0 R >>", shading + objs);
        Near(Px(viaPattern, 100, 100), (255, 0, 0), 0);
        Near(Px(viaPattern, 10, 100), (0, 255, 0), 0);
        Near(Px(viaPattern, 190, 100), (0, 255, 0), 0);
        var viaSh = Render("/S sh", "/Shading << /S 5 0 R >>", shading);
        Near(Px(viaSh, 100, 100), (255, 0, 0), 0);
        Near(Px(viaSh, 10, 100), (255, 255, 255), 0);
    }

    // ---- Coons / tensor patches (8.7.4.5.7-8)

    static string PatchBytes(int flag, (int X, int Y)[] pts, (int R, int G, int B)[] cols)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append((char)flag);
        foreach (var (x, y) in pts) sb.Append((char)x).Append((char)y);
        foreach (var (r, g, b) in cols) sb.Append((char)r).Append((char)g).Append((char)b);
        return sb.ToString();
    }

    static RenderedPage Patches(int type, string stream)
    {
        var objs = $"5 0 obj\n<< /ShadingType {type} /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 255 0 255 0 1 0 1 0 1] /Length {stream.Length} >>\nstream\n{stream}\nendstream\nendobj\n";
        var bytes = TestPdf.Build("/S sh", "/Shading << /S 5 0 R >>", objs, "[0 0 256 256]", brokenXref: true);
        return DocumentRenderer.RenderPage(PdfDocument.Load(bytes).Pages[0], 72);
    }

    [Fact]
    public void CoonsPatch_CurvedEdgeFollowsTheExactBezierBoundary()
    {
        // The left edge is a cubic through (64,64) (16,106) (16,149) (64,192): x(t) = 64 - 144 t (1 - t), y(t) = 64 + 128 t.
        (int, int)[] pts = [(64, 64), (16, 106), (16, 149), (64, 192), (106, 192), (149, 192), (192, 192), (192, 149), (192, 106), (192, 64), (149, 64), (106, 64)];
        var p = Patches(6, PatchBytes(0, pts, [(0, 0, 0), (0, 0, 0), (0, 0, 0), (0, 0, 0)]));
        foreach (var userY in new[] { 75, 90, 110, 128, 150, 170, 185 })
        {
            var t = (userY - 64) / 128.0;
            var edge = 64 - 144 * t * (1 - t);
            var py = 256 - userY;
            Near(Px(p, (int)Math.Floor(edge) - 1, py), (255, 255, 255), 0);
            Near(Px(p, (int)Math.Ceiling(edge) + 1, py), (0, 0, 0), 0);
        }
    }

    [Fact]
    public void TensorPatch_InteriorPointsShapeTheColourField()
    {
        (int, int)[] boundary = [(64, 64), (64, 106), (64, 149), (64, 192), (106, 192), (149, 192), (192, 192), (192, 149), (192, 106), (192, 64), (149, 64), (106, 64)];
        (int, int)[] flatInterior = [(106, 106), (106, 149), (149, 149), (149, 106)];
        (int, int)[] pulledInterior = [(106, 106), (106, 149), (175, 175), (149, 106)];
        (int, int, int)[] colours = [(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 0)];
        var flat = Patches(7, PatchBytes(0, [.. boundary, .. flatInterior], colours));
        var pulled = Patches(7, PatchBytes(0, [.. boundary, .. pulledInterior], colours));
        Assert.NotEqual(flat.Rgba, pulled.Rgba);
        // The boundary, and therefore the silhouette, does not depend on the interior control points.
        Near(Px(flat, 70, 128), Px(pulled, 70, 128), 12);
        Near(Px(flat, 40, 128), (255, 255, 255), 0);
        Near(Px(pulled, 40, 128), (255, 255, 255), 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CoonsPatch_EdgeFlagContinuationMatchesExplicitPatch(int flag)
    {
        (int X, int Y)[] a = [(40, 40), (40, 80), (40, 120), (40, 160), (80, 170), (120, 170), (160, 160), (165, 120), (165, 80), (160, 40), (120, 35), (80, 35)];
        (int R, int G, int B)[] ac = [(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 0)];
        (int, int)[] fresh = [(190, 200), (220, 230), (235, 250), (200, 245), (150, 250), (100, 245), (60, 230), (45, 200)];
        (int, int, int)[] freshColours = [(0, 255, 255), (255, 0, 255)];
        int[][] inherited = [[3, 4, 5, 6], [6, 7, 8, 9], [9, 10, 11, 0]];
        int[][] inheritedColours = [[1, 2], [2, 3], [3, 0]];
        var shared = inherited[flag - 1].Select(i => a[i]).ToArray();
        var sharedColours = inheritedColours[flag - 1].Select(i => ac[i]).ToArray();
        var first = PatchBytes(0, a, ac);
        var continued = Patches(6, first + PatchBytes(flag, fresh, freshColours));
        var explicitPatch = Patches(6, first + PatchBytes(0, [.. shared, .. fresh], [.. sharedColours, .. freshColours]));
        Assert.Equal(explicitPatch.Rgba, continued.Rgba);
        var alone = Patches(6, first);
        Assert.NotEqual(alone.Rgba, continued.Rgba);
    }

    // ---- Tiling patterns (8.7.3)

    static string TilingPattern(int obj, string dict, string content, string resources = "") =>
        $"{obj} 0 obj\n<< /PatternType 1 /Resources << {resources} >> {dict} /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n";

    [Fact]
    public void TilingPattern_NegativeStepsTileTheSameLatticeAsPositive()
    {
        const string cell = "1 0 0 rg 0 0 10 10 re f";
        var pos = Render("/Pattern cs /P scn 0 0 200 200 re f", "/Pattern << /P 5 0 R >>", TilingPattern(5, "/PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 20 /YStep 20", cell));
        var neg = Render("/Pattern cs /P scn 0 0 200 200 re f", "/Pattern << /P 5 0 R >>", TilingPattern(5, "/PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep -20 /YStep -20", cell));
        Assert.Equal(pos.Rgba, neg.Rgba);
        Near(Px(pos, 5, 195), (255, 0, 0), 0);
        Near(Px(pos, 15, 195), (255, 255, 255), 0);
        Near(Px(pos, 25, 175), (255, 0, 0), 0);
    }

    [Fact]
    public void TilingPattern_ReversedBBoxIsNormalised()
    {
        var p = Render("/Pattern cs /P scn 0 0 200 200 re f", "/Pattern << /P 5 0 R >>",
            TilingPattern(5, "/PaintType 1 /TilingType 1 /BBox [10 10 0 0] /XStep 20 /YStep 20", "1 0 0 rg 0 0 10 10 re f"));
        Near(Px(p, 5, 195), (255, 0, 0), 0);
        Near(Px(p, 15, 195), (255, 255, 255), 0);
    }

    [Fact]
    public void TilingPattern_StepSmallerThanBBoxOverlapsCells()
    {
        // 10x10 squares every 10 units inside a 20x20 cell: the overlapping cells cover the plane.
        var p = Render("/Pattern cs /P scn 0 0 200 200 re f", "/Pattern << /P 5 0 R >>",
            TilingPattern(5, "/PaintType 1 /TilingType 1 /BBox [0 0 20 20] /XStep 10 /YStep 10", "1 0 0 rg 0 0 10 10 re f"));
        foreach (var (x, y) in new[] { (3, 3), (13, 3), (3, 13), (57, 91), (190, 190) }) Near(Px(p, x, y), (255, 0, 0), 0);
    }

    [Fact]
    public void TilingPattern_RotatedMatrixMatchesAnalyticLattice()
    {
        // Matrix [0 1 -1 0 0 0] maps pattern (x, y) to (-y, x): squares occupy x in [20m-10, 20m], y in [20k, 20k+10].
        var p = Render("/Pattern cs /P scn 0 0 200 200 re f", "/Pattern << /P 5 0 R >>",
            TilingPattern(5, "/PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 20 /YStep 20 /Matrix [0 1 -1 0 0 0]", "1 0 0 rg 0 0 10 10 re f"));
        Near(Px(p, 15, 195), (255, 0, 0), 0);
        Near(Px(p, 5, 195), (255, 255, 255), 0);
        Near(Px(p, 15, 185), (255, 255, 255), 0);
        Near(Px(p, 35, 175), (255, 0, 0), 0);
    }

    [Fact]
    public void TilingPattern_SkewedMatrixPlacesCellsAlongTheSkewedAxes()
    {
        // Matrix [1 0 1 1 0 0]: pattern (x, y) -> (x + y, y). Cell (0,0) covers u = x + y in [y, y + 10] for y in [0, 10].
        var p = Render("/Pattern cs /P scn 0 0 200 200 re f", "/Pattern << /P 5 0 R >>",
            TilingPattern(5, "/PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 20 /YStep 20 /Matrix [1 0 1 1 0 0]", "1 0 0 rg 0 0 10 10 re f"));
        Near(Px(p, 12, 195), (255, 0, 0), 0);   // user (12.5, 4.5): x - y = 8 inside [0,10]
        Near(Px(p, 8, 195), (255, 0, 0), 0);
        Near(Px(p, 2, 195), (255, 255, 255), 0);  // x - y = -2 outside
        Near(Px(p, 12, 185), (255, 255, 255), 0); // y = 14.5 lies between cell rows
        Near(Px(p, 20, 195), (255, 255, 255), 0); // user (20.5, 4.5): x - y = 16 outside
    }

    [Fact]
    public void UncolouredTilingPattern_UsesSeparationUnderlyingColourAndIgnoresCellColourOperators()
    {
        const string cs = "[/Pattern [/Separation /Spot /DeviceRGB << /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [1 0 0] /N 1 >>]]";
        var p = Render("/CS cs 1 /P scn 0 0 200 200 re f", $"/ColorSpace << /CS {cs} >> /Pattern << /P 5 0 R >>",
            TilingPattern(5, "/PaintType 2 /TilingType 1 /BBox [0 0 10 10] /XStep 20 /YStep 20", "0 0 1 rg 0 0 10 10 re f"));
        Near(Px(p, 5, 195), (255, 0, 0), 2);
        Near(Px(p, 15, 195), (255, 255, 255), 0);
        var half = Render("/CS cs 0.5 /P scn 0 0 200 200 re f", $"/ColorSpace << /CS {cs} >> /Pattern << /P 5 0 R >>",
            TilingPattern(5, "/PaintType 2 /TilingType 1 /BBox [0 0 10 10] /XStep 20 /YStep 20", "0 0 10 10 re f"));
        Near(Px(half, 5, 195), (255, 128, 128), 3);
    }

    [Fact]
    public void NestedTilingPattern_IsPositionedInTheEnclosingCellsSpace()
    {
        // The outer cell origin is at x = 105; the inner pattern (red squares [0,10] every 20) is placed relative to that origin.
        var inner = TilingPattern(7, "/PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 20 /YStep 20", "1 0 0 rg 0 0 10 10 re f");
        var outer = TilingPattern(6, "/PaintType 1 /TilingType 1 /BBox [0 0 40 40] /XStep 200 /YStep 200 /Matrix [1 0 0 1 105 0]",
            "/Pattern cs /PB scn 0 0 20 20 re f", "/Pattern << /PB 7 0 R >>");
        var p = Render("/Pattern cs /P scn 0 0 200 200 re f", "/Pattern << /P 6 0 R >>", outer + inner);
        Assert.Empty(p.Warnings);
        Near(Px(p, 107, 195), (255, 0, 0), 0);
        Near(Px(p, 112, 195), (255, 0, 0), 0);
        Near(Px(p, 117, 195), (255, 255, 255), 0);
        Near(Px(p, 122, 195), (255, 255, 255), 0);
    }

    [Fact]
    public void SelfReferencingTilingPattern_TerminatesWithoutHanging()
    {
        var self = TilingPattern(5, "/PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 10 /YStep 10", "/Pattern cs /P scn 0 0 10 10 re f", "/Pattern << /P 5 0 R >>");
        var p = Render("/Pattern cs /P scn 0 0 40 40 re f", "/Pattern << /P 5 0 R >>", self);
        Assert.NotNull(p);
    }

    [Fact]
    public void PatternStroke_PaintsThePatternThroughTheStrokeOutline()
    {
        var p = Render("/Pattern CS /P SCN 20 w 0 100 m 200 100 l S", "/Pattern << /P 5 0 R >>",
            TilingPattern(5, "/PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 10 /YStep 10", "1 0 0 rg 0 0 10 10 re f"));
        Near(Px(p, 100, 100), (255, 0, 0), 0);
        Near(Px(p, 100, 92), (255, 0, 0), 0);
        Near(Px(p, 100, 80), (255, 255, 255), 0);
        Near(Px(p, 100, 120), (255, 255, 255), 0);
    }
}
