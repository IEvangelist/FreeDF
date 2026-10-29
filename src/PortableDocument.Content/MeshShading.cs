namespace PortableDocument.Content;

/// <summary>Decodes shading types 4-7 (free-form, lattice, Coons and tensor patch meshes) into Gouraud triangles in device space.</summary>
static class MeshShading
{
    public readonly record struct Vertex(double X, double Y, double[] C);

    public sealed class Mesh
    {
        public readonly List<(Vertex A, Vertex B, Vertex C)> Triangles = [];
    }

    sealed class Bits(byte[] data)
    {
        readonly byte[] _d = data; int _pos; // bit position
        public bool Eof(int need = 1) => _pos + need > _d.Length * 8;
        public ulong Read(int n)
        {
            ulong v = 0;
            for (var i = 0; i < n; i++, _pos++) v = (v << 1) | (uint)((_d[_pos >> 3] >> (7 - (_pos & 7))) & 1);
            return v;
        }
        public void Align() => _pos = (_pos + 7) & ~7;
    }

    public static Mesh Decode(int type, byte[] data, double[] decode, int bpCoord, int bpComp, int bpFlag, int nComps, int vertsPerRow, Matrix toDevice)
    {
        var mesh = new Mesh();
        if (decode.Length < 4 + 2 * nComps) return mesh;
        var br = new Bits(data);
        var cMax = bpCoord >= 64 ? ulong.MaxValue : (1UL << bpCoord) - 1;
        var kMax = bpComp >= 64 ? ulong.MaxValue : (1UL << bpComp) - 1;

        (double X, double Y) Pt()
        {
            var x = decode[0] + br.Read(bpCoord) * (decode[1] - decode[0]) / cMax;
            var y = decode[2] + br.Read(bpCoord) * (decode[3] - decode[2]) / cMax;
            return toDevice.Apply(x, y);
        }
        double[] Col()
        {
            var c = new double[nComps];
            for (var i = 0; i < nComps; i++) c[i] = decode[4 + 2 * i] + br.Read(bpComp) * (decode[5 + 2 * i] - decode[4 + 2 * i]) / kMax;
            return c;
        }
        Vertex Vtx() { var p = Pt(); return new Vertex(p.X, p.Y, Col()); }

        if (type == 4)
        {
            Vertex? a = null, b = null, c = null;
            var vertBits = bpFlag + 2 * bpCoord + nComps * bpComp;
            while (!br.Eof(vertBits))
            {
                var f = (int)br.Read(bpFlag);
                var v = Vtx(); br.Align();
                if (f == 0 || a is null)
                {
                    a = v;
                    if (br.Eof(vertBits)) break;
                    br.Read(bpFlag); b = Vtx(); br.Align();
                    if (br.Eof(vertBits)) break;
                    br.Read(bpFlag); c = Vtx(); br.Align();
                }
                else if (f == 1) { a = b; b = c; c = v; }
                else { b = c; c = v; }
                mesh.Triangles.Add((a!.Value, b!.Value, c!.Value));
            }
        }
        else if (type == 5)
        {
            if (vertsPerRow < 2) return mesh;
            var rows = new List<Vertex[]>();
            var vertBits = 2 * bpCoord + nComps * bpComp;
            while (!br.Eof(vertBits * vertsPerRow))
            {
                var row = new Vertex[vertsPerRow];
                for (var i = 0; i < vertsPerRow; i++) { row[i] = Vtx(); br.Align(); } // each vertex is padded to a byte boundary
                rows.Add(row);
            }
            for (var r = 0; r + 1 < rows.Count; r++)
                for (var i = 0; i + 1 < vertsPerRow; i++)
                {
                    mesh.Triangles.Add((rows[r][i], rows[r][i + 1], rows[r + 1][i]));
                    mesh.Triangles.Add((rows[r][i + 1], rows[r + 1][i + 1], rows[r + 1][i]));
                }
        }
        else
        {
            var nPts = type == 6 ? 12 : 16;
            var p = new (double X, double Y)[4, 4];
            var c = new double[4][]; // c00, c03, c33, c30
            var have = false;
            while (!br.Eof(bpFlag))
            {
                var f = (int)br.Read(bpFlag) & 3;
                int skipPts = f == 0 || !have ? 0 : 4, skipCols = f == 0 || !have ? 0 : 2;
                if (br.Eof((nPts - skipPts) * 2 * bpCoord + (4 - skipCols) * nComps * bpComp)) break;
                var pp = new (double X, double Y)[4, 4];
                var cc = new double[4][];
                // Stream order of boundary points as (i,j).
                ReadOnlySpan<(int, int)> order = [(0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2), (3, 1), (3, 0), (2, 0), (1, 0)];
                var first = 0;
                if (skipPts > 0)
                {
                    (double, double)[] e;
                    switch (f)
                    {
                        case 1: e = [p[0, 3], p[1, 3], p[2, 3], p[3, 3]]; cc[0] = c[1]; cc[1] = c[2]; break;
                        case 2: e = [p[3, 3], p[3, 2], p[3, 1], p[3, 0]]; cc[0] = c[2]; cc[1] = c[3]; break;
                        default: e = [p[3, 0], p[2, 0], p[1, 0], p[0, 0]]; cc[0] = c[3]; cc[1] = c[0]; break;
                    }
                    for (var i = 0; i < 4; i++) pp[order[i].Item1, order[i].Item2] = e[i];
                    first = 4;
                }
                for (var i = first; i < 12; i++) pp[order[i].Item1, order[i].Item2] = Pt();
                if (type == 7)
                {
                    pp[1, 1] = Pt(); pp[1, 2] = Pt(); pp[2, 2] = Pt(); pp[2, 1] = Pt();
                }
                else
                {
                    pp[1, 1] = Interior(pp[0, 0], pp[0, 1], pp[1, 0], pp[0, 3], pp[3, 0], pp[3, 1], pp[1, 3], pp[3, 3]);
                    pp[1, 2] = Interior(pp[0, 3], pp[0, 2], pp[1, 3], pp[0, 0], pp[3, 3], pp[3, 2], pp[1, 0], pp[3, 0]);
                    pp[2, 1] = Interior(pp[3, 0], pp[3, 1], pp[2, 0], pp[3, 3], pp[0, 0], pp[0, 1], pp[2, 3], pp[0, 3]);
                    pp[2, 2] = Interior(pp[3, 3], pp[3, 2], pp[2, 3], pp[3, 0], pp[0, 3], pp[0, 2], pp[2, 0], pp[0, 0]);
                }
                for (var i = skipCols; i < 4; i++) cc[i] = Col();
                br.Align();
                p = pp; c = cc; have = true;
                // Subdivide in proportion to the patch's device size so large patches stay smooth.
                double mnx = double.MaxValue, mxx = double.MinValue, mny = double.MaxValue, mxy = double.MinValue;
                foreach (var q in p) { mnx = Math.Min(mnx, q.X); mxx = Math.Max(mxx, q.X); mny = Math.Min(mny, q.Y); mxy = Math.Max(mxy, q.Y); }
                var span = Math.Max(mxx - mnx, mxy - mny);
                Tessellate(mesh, p, c, double.IsFinite(span) ? Math.Clamp((int)Math.Ceiling(span / 8), 8, 40) : 8);
            }
        }
        return mesh;
    }

    // 1/9 [-4 a + 6 (b + c) - 2 (d + e) + 3 (f + g) - h]
    static (double X, double Y) Interior((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d,
        (double X, double Y) e, (double X, double Y) f, (double X, double Y) g, (double X, double Y) h)
        => ((-4 * a.X + 6 * (b.X + c.X) - 2 * (d.X + e.X) + 3 * (f.X + g.X) - h.X) / 9,
            (-4 * a.Y + 6 * (b.Y + c.Y) - 2 * (d.Y + e.Y) + 3 * (f.Y + g.Y) - h.Y) / 9);

    static void Tessellate(Mesh mesh, (double X, double Y)[,] p, double[][] c, int n)
    {
        var grid = new Vertex[n + 1, n + 1];
        var bu = new double[4]; var bv = new double[4];
        for (var iu = 0; iu <= n; iu++)
        {
            var u = (double)iu / n; Bern(u, bu);
            for (var iv = 0; iv <= n; iv++)
            {
                var v = (double)iv / n; Bern(v, bv);
                double x = 0, y = 0;
                for (var i = 0; i < 4; i++)
                    for (var j = 0; j < 4; j++) { var w = bu[i] * bv[j]; x += w * p[i, j].X; y += w * p[i, j].Y; }
                var col = new double[c[0].Length];
                for (var k = 0; k < col.Length; k++)
                    col[k] = (1 - u) * ((1 - v) * c[0][k] + v * c[1][k]) + u * ((1 - v) * c[3][k] + v * c[2][k]);
                grid[iu, iv] = new Vertex(x, y, col);
            }
        }
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
            {
                mesh.Triangles.Add((grid[i, j], grid[i + 1, j], grid[i, j + 1]));
                mesh.Triangles.Add((grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]));
            }
    }

    static void Bern(double t, double[] b)
    {
        var s = 1 - t;
        b[0] = s * s * s; b[1] = 3 * t * s * s; b[2] = 3 * t * t * s; b[3] = t * t * t;
    }

    /// <summary>Rasterizes the mesh into a device-space colour buffer; <paramref name="toRgb"/> maps interpolated components to RGB.</summary>
    public static Func<double, double, RgbColor?> ToShader(Mesh mesh, Func<double[], RgbColor> toRgb)
    {
        if (mesh.Triangles.Count == 0) return static (_, _) => null;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (a, b, c) in mesh.Triangles)
            foreach (var v in (ReadOnlySpan<Vertex>)[a, b, c])
            { minX = Math.Min(minX, v.X); maxX = Math.Max(maxX, v.X); minY = Math.Min(minY, v.Y); maxY = Math.Max(maxY, v.Y); }
        int bx0 = Math.Max(0, (int)Math.Floor(minX)), by0 = Math.Max(0, (int)Math.Floor(minY));
        int bx1 = Math.Min(16384, (int)Math.Ceiling(maxX)), by1 = Math.Min(16384, (int)Math.Ceiling(maxY));
        int w = bx1 - bx0, h = by1 - by0;
        if (w <= 0 || h <= 0 || (long)w * h > 100_000_000) return static (_, _) => null;
        var buf = new RgbColor?[w * h];
        foreach (var (a, b, c) in mesh.Triangles)
        {
            var det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (Math.Abs(det) < 1e-12) continue;
            int x0 = Math.Max(bx0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), x1 = Math.Min(bx1 - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
            int y0 = Math.Max(by0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))), y1 = Math.Min(by1 - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
            var n = a.C.Length; var tmp = new double[n];
            const double eps = -1e-3; // slight overdraw closes seams between triangles
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    double px = x + 0.5, py = y + 0.5;
                    var l1 = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / det;
                    var l2 = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / det;
                    var l3 = 1 - l1 - l2;
                    if (l1 < eps || l2 < eps || l3 < eps) continue;
                    for (var k = 0; k < n; k++) tmp[k] = l1 * a.C[k] + l2 * b.C[k] + l3 * c.C[k];
                    buf[(y - by0) * w + (x - bx0)] = toRgb(tmp);
                }
        }
        return (dx, dy) =>
        {
            int x = (int)Math.Floor(dx) - bx0, y = (int)Math.Floor(dy) - by0;
            return (uint)x < (uint)w && (uint)y < (uint)h ? buf[y * w + x] : null;
        };
    }
}
