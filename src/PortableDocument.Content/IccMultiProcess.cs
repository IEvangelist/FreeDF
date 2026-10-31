using System.Buffers.Binary;

namespace PortableDocument.Content;

/// <summary>
/// ICC.1:2010 (v4.3) multiProcessElements ('mpet') pipelines used by the D2Bx / B2Dx tags. Supports curve sets ('cvst' with
/// 'curf' segments of 'parf' formula types 0-2 and middle 'samf' sampled segments), matrices ('matf') and float CLUTs ('clut').
/// Values are real numbers (PCS XYZ as XYZ, PCS Lab as L* 0-100 and a*/b* -128..127), device values are 0..1.
/// Anything else (including the optional 'bACS'/'eACS' elements) makes <see cref="TryRead"/> return null so the caller falls
/// back to the A2Bx/B2Ax tags. All counts are bounded; no allocation on the evaluation path.
/// </summary>
internal sealed class IccMultiProcess
{
    const int MaxElements = 32, MaxChannels = 16, MaxSegments = 256, MaxSamples = 1 << 20, MaxClutValues = 16_000_000;

    abstract class Stage
    {
        public int In, Out;
        public abstract void Eval(ReadOnlySpan<float> x, Span<float> y);
    }

    public int In { get; private set; }
    public int Out { get; private set; }
    Stage[] _stages = [];

    public void Eval(ReadOnlySpan<double> input, Span<float> output)
    {
        Span<float> a = stackalloc float[MaxChannels];
        Span<float> b = stackalloc float[MaxChannels];
        for (var i = 0; i < In; i++) a[i] = (float)Math.Clamp(input[i], 0, 1);
        foreach (var s in _stages)
        {
            s.Eval(a, b);
            var t = a; a = b; b = t;
        }
        for (var i = 0; i < Out; i++) output[i] = a[i];
    }

    public static IccMultiProcess? TryRead(byte[] data, int off, int size, int nIn, int nOut)
    {
        try { return Read(data, off, size, nIn, nOut); } catch (Exception e) when (e is ArgumentException or InvalidDataException or OverflowException) { return null; }
    }

    static IccMultiProcess? Read(byte[] data, int off, int size, int nIn, int nOut)
    {
        ReadOnlySpan<byte> d = data.AsSpan(off, size);
        if (size < 16 || BinaryPrimitives.ReadUInt32BigEndian(d) != 0x6D706574) return null; // mpet
        int cin = BinaryPrimitives.ReadUInt16BigEndian(d[8..]), cout = BinaryPrimitives.ReadUInt16BigEndian(d[10..]);
        var count = BinaryPrimitives.ReadUInt32BigEndian(d[12..]);
        if (cin != nIn || cout != nOut || count == 0 || count > MaxElements || 16 + (long)count * 8 > size) return null;
        var stages = new Stage[count];
        var channels = cin;
        for (var i = 0; i < count; i++)
        {
            var eo = BinaryPrimitives.ReadUInt32BigEndian(d[(16 + 8 * i)..]);
            var es = BinaryPrimitives.ReadUInt32BigEndian(d[(20 + 8 * i)..]);
            if (eo > size || es < 12 || eo + (long)es > size) return null;
            var e = d.Slice((int)eo, (int)es);
            int ein = BinaryPrimitives.ReadUInt16BigEndian(e[8..]), eout = BinaryPrimitives.ReadUInt16BigEndian(e[10..]);
            if (ein != channels || ein is < 1 or > MaxChannels || eout is < 1 or > MaxChannels) return null;
            Stage? st = BinaryPrimitives.ReadUInt32BigEndian(e) switch
            {
                0x63767374 => ReadCurveSet(e, ein, eout), // cvst
                0x6D617466 => ReadMatrix(e, ein, eout), // matf
                0x636C7574 => ReadClut(e, ein, eout), // clut
                _ => null,
            };
            if (st is null) return null;
            st.In = ein; st.Out = eout;
            stages[i] = st;
            channels = eout;
        }
        if (channels != cout) return null;
        return new IccMultiProcess { In = cin, Out = cout, _stages = stages };
    }

    static float F(ReadOnlySpan<byte> d, int o) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(d[o..]));

    // -------------------------------------------------------------- matf

    sealed class MatrixStage(float[] m) : Stage
    {
        public override void Eval(ReadOnlySpan<float> x, Span<float> y)
        {
            for (var r = 0; r < Out; r++)
            {
                var s = m[In * Out + r];
                for (var c = 0; c < In; c++) s += m[r * In + c] * x[c];
                y[r] = s;
            }
        }
    }

    static Stage? ReadMatrix(ReadOnlySpan<byte> e, int ein, int eout)
    {
        var n = ein * eout + eout;
        if (12 + 4L * n > e.Length) return null;
        var m = new float[n];
        for (var i = 0; i < n; i++) if (!float.IsFinite(m[i] = F(e, 12 + 4 * i))) return null;
        return new MatrixStage(m);
    }

    // -------------------------------------------------------------- clut

    sealed class ClutStage(int[] grid, float[] v) : Stage
    {
        public override void Eval(ReadOnlySpan<float> x, Span<float> y)
        {
            Span<int> i0 = stackalloc int[MaxChannels]; Span<int> stride = stackalloc int[MaxChannels]; Span<float> fr = stackalloc float[MaxChannels];
            var s = Out;
            for (var d = In - 1; d >= 0; d--) { stride[d] = s; s *= grid[d]; }
            for (var d = 0; d < In; d++)
            {
                var g = grid[d];
                var p = Math.Clamp(float.IsNaN(x[d]) ? 0f : x[d], 0f, 1f) * (g - 1);
                var i = Math.Min((int)p, Math.Max(g - 2, 0));
                i0[d] = i; fr[d] = g > 1 ? p - i : 0;
            }
            for (var k = 0; k < Out; k++) y[k] = 0;
            for (var corner = 0; corner < 1 << In; corner++)
            {
                float w = 1; var idx = 0;
                for (var d = 0; d < In; d++)
                {
                    var bit = (corner >> d) & 1;
                    w *= bit == 1 ? fr[d] : 1 - fr[d];
                    idx += (i0[d] + (bit == 1 && grid[d] > 1 ? 1 : 0)) * stride[d];
                }
                if (w == 0) continue;
                for (var k = 0; k < Out; k++) y[k] += w * v[idx + k];
            }
        }
    }

    static Stage? ReadClut(ReadOnlySpan<byte> e, int ein, int eout)
    {
        if (ein > 5 || e.Length < 28) return null;
        var grid = new int[ein];
        long len = eout;
        for (var i = 0; i < ein; i++)
        {
            grid[i] = e[12 + i];
            if (grid[i] < 2) return null;
            len *= grid[i];
            if (len > MaxClutValues) return null;
        }
        if (28 + 4 * len > e.Length) return null;
        var v = new float[len];
        for (var i = 0; i < len; i++) if (!float.IsFinite(v[i] = F(e, 28 + 4 * i))) return null;
        return new ClutStage(grid, v);
    }

    // -------------------------------------------------------------- cvst

    sealed class Segment
    {
        public int Type; // 0..2 parf, 3 sampled
        public float[] P = [];
        public float Start, End; // sampled domain
    }

    sealed class SegCurve
    {
        public float[] Breaks = [];
        public Segment[] Segments = [];

        public float Eval(float x)
        {
            var i = 0;
            while (i < Breaks.Length && x >= Breaks[i]) i++;
            return EvalSegment(i, x);
        }

        public float EvalSegment(int i, float x)
        {
            var s = Segments[i];
            var p = s.P;
            switch (s.Type)
            {
                case 0: return (float)(Math.Pow(Math.Max(0, p[1] * x + p[2]), p[0]) + p[3]);
                case 1: { var t = p[2] * Math.Pow(Math.Max(x, 0), p[0]) + p[3]; return t > 0 ? (float)(p[1] * Math.Log10(t) + p[4]) : p[4]; }
                case 2: return (float)(p[0] * Math.Pow(p[1], p[2] * x + p[3]) + p[4]);
                default:
                    {
                        var u = (x - s.Start) / (s.End - s.Start) * (p.Length - 1);
                        var j = Math.Clamp((int)u, 0, p.Length - 2);
                        return p[j] + (p[j + 1] - p[j]) * (u - j);
                    }
            }
        }
    }

    sealed class CurveSetStage(SegCurve[] curves) : Stage
    {
        public override void Eval(ReadOnlySpan<float> x, Span<float> y)
        {
            for (var i = 0; i < In; i++)
            {
                var v = curves[i].Eval(float.IsNaN(x[i]) ? 0f : x[i]);
                y[i] = float.IsFinite(v) ? v : 0f;
            }
        }
    }

    static Stage? ReadCurveSet(ReadOnlySpan<byte> e, int ein, int eout)
    {
        if (ein != eout || 12 + 8L * ein > e.Length) return null;
        var curves = new SegCurve[ein];
        for (var i = 0; i < ein; i++)
        {
            var co = BinaryPrimitives.ReadUInt32BigEndian(e[(12 + 8 * i)..]);
            var cs = BinaryPrimitives.ReadUInt32BigEndian(e[(16 + 8 * i)..]);
            if (co > e.Length || cs < 12 || co + (long)cs > e.Length) return null;
            if ((curves[i] = ReadSegCurve(e.Slice((int)co, (int)cs))!) is null) return null;
        }
        return new CurveSetStage(curves);
    }

    static SegCurve? ReadSegCurve(ReadOnlySpan<byte> c)
    {
        if (BinaryPrimitives.ReadUInt32BigEndian(c) != 0x63757266) return null; // curf
        int n = BinaryPrimitives.ReadUInt16BigEndian(c[8..]);
        if (n < 1 || n > MaxSegments || 12 + 4L * (n - 1) > c.Length) return null;
        var br = new float[n - 1];
        for (var i = 0; i < br.Length; i++)
        {
            br[i] = F(c, 12 + 4 * i);
            if (!float.IsFinite(br[i]) || (i > 0 && br[i] <= br[i - 1])) return null;
        }
        var segs = new Segment[n];
        var p = 12 + 4 * (n - 1);
        for (var i = 0; i < n; i++)
        {
            if (p + 12 > c.Length) return null;
            var sig = BinaryPrimitives.ReadUInt32BigEndian(c[p..]);
            if (sig == 0x70617266) // parf
            {
                int type = BinaryPrimitives.ReadUInt16BigEndian(c[(p + 8)..]);
                int[] np = [4, 5, 5];
                if (type > 2 || p + 12 + 4 * np[type] > c.Length) return null;
                var q = new float[5];
                for (var k = 0; k < np[type]; k++) if (!float.IsFinite(q[k] = F(c, p + 12 + 4 * k))) return null;
                segs[i] = new Segment { Type = type, P = q };
                p += 12 + 4 * np[type];
            }
            else if (sig == 0x73616D66) // samf
            {
                // Needs finite bounds, and its implicit first sample is the previous segment's value at the start boundary.
                if (i == 0 || i == n - 1) return null;
                var cnt = BinaryPrimitives.ReadUInt32BigEndian(c[(p + 8)..]);
                if (cnt < 1 || cnt > MaxSamples || p + 12 + 4L * cnt > c.Length) return null;
                var q = new float[cnt + 1];
                for (var k = 0; k < cnt; k++) if (!float.IsFinite(q[k + 1] = F(c, p + 12 + 4 * k))) return null;
                segs[i] = new Segment { Type = 3, P = q, Start = br[i - 1], End = br[i] };
                p += 12 + 4 * (int)cnt;
            }
            else return null;
        }
        var sc = new SegCurve { Breaks = br, Segments = segs };
        for (var i = 1; i < n - 1; i++)
            if (segs[i].Type == 3) segs[i].P[0] = sc.EvalSegment(i - 1, br[i - 1]);
        return sc;
    }
}
