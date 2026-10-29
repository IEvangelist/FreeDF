namespace PortableDocument.Content;

/// <summary>Bit reader for packet headers (T.800 B.10.1): a zero bit is stuffed after every 0xFF byte.</summary>
internal sealed class JpxBits
{
    readonly byte[] _d;
    readonly int _end;
    int _pos, _ct, _cur;
    public bool Overrun;

    public JpxBits(byte[] d, int pos, int end) { _d = d; _pos = pos; _end = end; }

    public int Bit()
    {
        if (_ct == 0)
        {
            var prev = _cur;
            if (_pos >= _end) { Overrun = true; _cur = 0; }
            else _cur = _d[_pos++];
            _ct = prev == 0xFF ? 7 : 8;
        }
        _ct--;
        return _cur >> _ct & 1;
    }

    public int Bits(int n)
    {
        var v = 0;
        while (n-- > 0) v = v << 1 | Bit();
        return v;
    }

    /// <summary>Finishes the header and returns the position of the following byte.</summary>
    public int Align()
    {
        if (_cur == 0xFF && _pos < _end) _pos++;
        _ct = 0; _cur = 0;
        return _pos;
    }
}

internal sealed class JpxTagTree
{
    readonly int[] _val, _low, _off, _lw;
    readonly int _levels;

    public JpxTagTree(int w, int h)
    {
        var lw = new List<int>(); var off = new List<int>();
        var total = 0;
        while (true)
        {
            lw.Add(w); off.Add(total); total += w * h;
            if (w <= 1 && h <= 1) break;
            w = (w + 1) >> 1; h = (h + 1) >> 1;
        }
        _levels = lw.Count; _lw = [.. lw]; _off = [.. off];
        _val = new int[total]; _low = new int[total];
        Array.Fill(_val, int.MaxValue);
    }

    public bool Decode(JpxBits br, int x, int y, int threshold)
    {
        var low = 0;
        var idx = 0;
        for (var l = _levels - 1; l >= 0; l--)
        {
            idx = _off[l] + (y >> l) * _lw[l] + (x >> l);
            if (low > _low[idx]) _low[idx] = low; else low = _low[idx];
            while (low < threshold && low < _val[idx])
            {
                if (br.Bit() == 1) _val[idx] = low; else low++;
            }
            _low[idx] = low;
        }
        return _val[idx] < threshold;
    }
}

internal sealed class JpxCodParams
{
    public int NL, Xcb, Ycb, Style, Transform;
    public int[] PPx = [], PPy = [];
}

internal sealed class JpxQuant
{
    public int Style, Guard;
    public int[] Exp = [], Mant = [];
}

internal readonly record struct JpxPoc(int ResStart, int CompStart, int LayerEnd, int ResEnd, int CompEnd, int Prog);

/// <summary>JPEG 2000 codestream decoder (T.800 Annex A-G, I): tier-2 packets, tier-1 blocks, dequantisation, inverse DWT and MCT.</summary>
internal sealed class JpxCodestream
{
    public int Xsiz, Ysiz, XOsiz, YOsiz, XTsiz, YTsiz, XTOsiz, YTOsiz, Csiz;
    public int[] Depth = [], Xr = [], Yr = [], CW = [], CH = [];
    public bool[] Signed = [];
    public ushort[][] Planes = [];
    public string? Warning;

    sealed class TileState
    {
        public MemoryStream Data = new();
        public readonly List<TilePartData> Parts = [];
        public bool HeaderDone;
        public int Prog = -1, Layers, Mct;
        public bool Sop, Eph;
        public JpxCodParams? Cod;
        public bool HasPoc;
        public readonly List<JpxPoc> Pocs = [];
        public readonly Dictionary<int, JpxCodParams> Coc = [];
        public JpxQuant? Qcd;
        public readonly Dictionary<int, JpxQuant> Qcc = [];
        public readonly Dictionary<int, int> Roi = [];
    }

    sealed record TilePartData(int Start, int Length, byte[]? PacketHeaders);

    sealed class PackedHeaderException(string message) : Exception(message);

    // main header defaults
    int _prog, _layers, _mct;
    bool _sop, _eph;
    JpxCodParams? _cod;
    readonly List<JpxPoc> _pocs = [];
    readonly Dictionary<int, JpxCodParams> _coc = [];
    JpxQuant? _qcd;
    readonly Dictionary<int, JpxQuant> _qcc = [];
    readonly Dictionary<int, int> _roi = [];
    byte[] _d = [];
    int _ntx, _nty;

    sealed class TComp
    {
        public int X0, Y0, X1, Y1, Roi;
        public JpxCodParams Cod = null!;
        public JpxQuant Q = null!;
        public TRes[] Res = [];
        public float[] Out = [];
    }

    sealed class TRes
    {
        public int X0, Y0, X1, Y1, PPx, PPy, Npw, Nph;
        public TBand[] Bands = [];
        public TPrec[] Prec = [];
    }

    sealed class TBand
    {
        public int Orient, X0, Y0, X1, Y1, Mb;
        public float Step;
        public float[] Coef = [];
    }

    sealed class TPrec
    {
        public TPrecBand[] PB = [];
    }

    sealed class TPrecBand
    {
        public int Ncw, Nch;
        public JpxCodeBlock[] Blocks = [];
        public JpxTagTree? Incl, Zbp;
    }

    static int CeilDiv(int a, int b) => (int)(((long)a + b - 1) / b);
    static int CeilShr(int a, int s) => (int)(((long)a + (1L << s) - 1) >> s);
    int U16(int p) => _d[p] << 8 | _d[p + 1];
    int U32(int p) => _d[p] << 24 | _d[p + 1] << 16 | _d[p + 2] << 8 | _d[p + 3];

    public bool Decode(byte[] d, int start, int end)
    {
        _d = d;
        var p = start;
        if (end - p < 4 || U16(p) != 0xFF4F) { Warning = "JPX: missing SOC marker"; return false; }
        p += 2;
        var tiles = new Dictionary<int, TileState>();
        var ppmMarkers = new SortedDictionary<int, byte[]>();
        var sizSeen = false;
        var mainPocSeen = false;
        while (p + 2 <= end)
        {
            var m = U16(p);
            if (m == 0xFF90) break;
            if (m == 0xFFD9)
            {
                if (ppmMarkers.Count > 0) { Warning = "JPX: PPM data has no tile-part"; return false; }
                return Finish(tiles);
            }
            if (p + 4 > end) { Warning = "JPX: truncated main-header marker segment"; return false; }
            var len = U16(p + 2);
            if (len < 2 || (long)p + 2 + len > end) { Warning = "JPX: invalid main-header marker segment length"; return false; }
            var body = p + 4;
            switch (m)
            {
                case 0xFF51: ParseSiz(body); sizSeen = true; break;
                case 0xFF52: ParseCod(body, len - 2, out _prog, out _layers, out _mct, out _sop, out _eph, out _cod); break;
                case 0xFF53: ParseCoc(body, len - 2, _coc); break;
                case 0xFF5C: _qcd = ParseQuant(body, len - 2); break;
                case 0xFF5D: ParseQcc(body, len - 2, _qcc); break;
                case 0xFF5E: ParseRgn(body, _roi); break;
                case 0xFF61: Warning = "JPX: PPT marker segment is only valid in a tile-part header"; return false;
                case 0xFF60:
                    if (len < 4) { Warning = "JPX: malformed PPM marker segment"; return false; }
                    var ppmIndex = _d[body];
                    if (ppmMarkers.ContainsKey(ppmIndex)) { Warning = $"JPX: duplicate PPM marker index {ppmIndex}"; return false; }
                    ppmMarkers.Add(ppmIndex, _d.AsSpan(body + 1, len - 3).ToArray());
                    break;
                case 0xFF5F:
                    if (!sizSeen) throw new InvalidDataException("JPX: malformed POC marker (before SIZ)");
                    if (mainPocSeen) throw new InvalidDataException("JPX: malformed POC marker (multiple markers in main header)");
                    if (len < 2 || p + 2L + len > end) throw new InvalidDataException("JPX: malformed POC marker (truncated segment)");
                    ParsePoc(body, len - 2, _pocs);
                    mainPocSeen = true;
                    break;
            }
            p += 2 + len;
        }
        if (!sizSeen || _cod is null || _qcd is null) { Warning = "JPX: incomplete main header"; return false; }
        if (Csiz is < 1 or > 16384 || (long)(Xsiz - XOsiz) * (Ysiz - YOsiz) > 400_000_000L) { Warning = "JPX: unsupported image size"; return false; }
        if (!HasContiguousIndices(ppmMarkers, out var missingPpmIndex)) { Warning = $"JPX: missing PPM marker index {missingPpmIndex}"; return false; }
        var ppmData = JoinPackedSegments(ppmMarkers);
        var ppmPos = 0;
        _ntx = CeilDiv(Xsiz - XTOsiz, XTsiz); _nty = CeilDiv(Ysiz - YTOsiz, YTsiz);
        CW = new int[Csiz]; CH = new int[Csiz]; Planes = new ushort[Csiz][];
        for (var c = 0; c < Csiz; c++)
        {
            CW[c] = CeilDiv(Xsiz, Xr[c]) - CeilDiv(XOsiz, Xr[c]);
            CH[c] = CeilDiv(Ysiz, Yr[c]) - CeilDiv(YOsiz, Yr[c]);
            Planes[c] = new ushort[(long)CW[c] * CH[c]];
        }
        while (p + 2 <= end && U16(p) == 0xFF90)
        {
            var sot = p;
            if (p + 12 > end || U16(p + 2) != 10) { Warning = "JPX: malformed SOT marker segment"; return false; }
            var isot = U16(p + 4);
            long psot = (uint)U32(p + 6);
            if (psot != 0 && psot < 14) { Warning = "JPX: invalid tile-part length"; return false; }
            if (psot != 0 && (long)sot + psot > end) { Warning = "JPX: truncated tile-part"; return false; }
            p += 12;
            var tpEnd = psot == 0 ? end : (int)Math.Min(end, sot + psot);
            if (psot == 0 && tpEnd - 2 >= p && U16(tpEnd - 2) == 0xFFD9) tpEnd -= 2;
            byte[]? ppmPartHeaders = null;
            if (ppmData is not null)
            {
                if (!TryTakePpmHeaders(ppmData, ref ppmPos, out ppmPartHeaders, out var error))
                {
                    Warning = "JPX: " + error;
                    return false;
                }
            }
            if (isot >= _ntx * _nty) { p = tpEnd; continue; }
            if (!tiles.TryGetValue(isot, out var ts)) tiles[isot] = ts = new TileState();
            var pptMarkers = new SortedDictionary<int, byte[]>();
            var sodSeen = false;
            var tilePartPocSeen = false;
            while (p + 2 <= tpEnd)
            {
                var m = U16(p);
                if (m == 0xFF93) { p += 2; sodSeen = true; break; }
                if (p + 4 > tpEnd) { Warning = "JPX: truncated tile-part marker segment"; return false; }
                var len = U16(p + 2);
                if (len < 2 || (long)p + 2 + len > tpEnd) { Warning = "JPX: invalid tile-part marker segment length"; return false; }
                var body = p + 4;
                switch (m)
                {
                    case 0xFF52:
                        ParseCod(body, len - 2, out ts.Prog, out ts.Layers, out ts.Mct, out ts.Sop, out ts.Eph, out ts.Cod);
                        break;
                    case 0xFF53: ParseCoc(body, len - 2, ts.Coc); break;
                    case 0xFF5C: ts.Qcd = ParseQuant(body, len - 2); break;
                    case 0xFF5D: ParseQcc(body, len - 2, ts.Qcc); break;
                    case 0xFF5E: ParseRgn(body, ts.Roi); break;
                    case 0xFF60: Warning = "JPX: PPM marker segment is only valid in the main header"; return false;
                    case 0xFF61:
                        if (ppmData is not null) { Warning = "JPX: PPM and PPT markers cannot be combined"; return false; }
                        if (len < 4) { Warning = "JPX: malformed PPT marker segment"; return false; }
                        var pptIndex = _d[body];
                        if (pptMarkers.ContainsKey(pptIndex)) { Warning = $"JPX: duplicate PPT marker index {pptIndex}"; return false; }
                        pptMarkers.Add(pptIndex, _d.AsSpan(body + 1, len - 3).ToArray());
                        break;
                    case 0xFF5F:
                        if (tilePartPocSeen) throw new InvalidDataException("JPX: malformed POC marker (multiple markers in tile-part header)");
                        if (len < 2 || p + 2L + len > tpEnd) throw new InvalidDataException("JPX: malformed POC marker (truncated segment)");
                        if (!ts.HasPoc && _d[sot + 10] != 0) throw new InvalidDataException("JPX: malformed POC marker (tile POC is not in the first tile-part)");
                        ParsePoc(body, len - 2, ts.Pocs);
                        ts.HasPoc = true;
                        tilePartPocSeen = true;
                        break;
                }
                p += 2 + len;
            }
            if (!sodSeen) { Warning = "JPX: missing SOD marker"; return false; }
            if (!HasContiguousIndices(pptMarkers, out var missingPptIndex)) { Warning = $"JPX: missing PPT marker index {missingPptIndex}"; return false; }
            var packetHeaders = ppmPartHeaders ?? JoinPackedSegments(pptMarkers);
            ts.Parts.Add(new TilePartData(p, tpEnd - p, ppmData is not null || pptMarkers.Count > 0 ? packetHeaders : null));
            p = tpEnd;
        }
        if (ppmData is not null && ppmPos != ppmData.Length) { Warning = "JPX: unused PPM packet-header data"; return false; }
        foreach (var ts in tiles.Values)
        {
            if (ts.Parts.Exists(part => part.PacketHeaders is not null)) continue;
            foreach (var part in ts.Parts) ts.Data.Write(_d, part.Start, part.Length);
            ts.Parts.Clear();
        }
        return Finish(tiles);
    }

    static byte[]? JoinPackedSegments(SortedDictionary<int, byte[]> segments)
    {
        if (segments.Count == 0) return null;
        using var joined = new MemoryStream();
        foreach (var segment in segments.Values) joined.Write(segment);
        return joined.ToArray();
    }

    static bool HasContiguousIndices(SortedDictionary<int, byte[]> segments, out int missingIndex)
    {
        missingIndex = 0;
        foreach (var index in segments.Keys)
        {
            if (index != missingIndex) return false;
            missingIndex++;
        }
        return true;
    }

    static bool TryTakePpmHeaders(byte[] data, ref int pos, out byte[] headers, out string error)
    {
        headers = [];
        error = "";
        if (data.Length - pos < 4) { error = "truncated PPM Nppm field"; return false; }
        var length = (uint)(data[pos] << 24 | data[pos + 1] << 16 | data[pos + 2] << 8 | data[pos + 3]);
        pos += 4;
        if (length > data.Length - pos) { error = "truncated PPM packet-header payload"; return false; }
        headers = data.AsSpan(pos, (int)length).ToArray();
        pos += (int)length;
        return true;
    }

    void ParsePoc(int p, int len, List<JpxPoc> into)
    {
        var componentBytes = Csiz < 257 ? 1 : 2;
        var entrySize = 5 + 2 * componentBytes;
        if (len <= 0 || len % entrySize != 0 || p < 0 || p > _d.Length - len)
            throw new InvalidDataException("JPX: malformed POC marker (invalid segment length)");

        var end = p + len;
        while (p < end)
        {
            var resStart = _d[p++];
            var compStart = componentBytes == 1 ? _d[p++] : U16(p);
            if (componentBytes == 2) p += 2;
            var layerEnd = U16(p); p += 2;
            var resEnd = _d[p++];
            var compEnd = componentBytes == 1 ? _d[p++] : U16(p);
            if (componentBytes == 2) p += 2;
            if (componentBytes == 1 && compEnd == 0) compEnd = 256;
            var prog = _d[p++];

            if (layerEnd == 0 || resStart >= resEnd || compStart >= compEnd || prog > 4)
                throw new InvalidDataException("JPX: malformed POC marker (invalid progression range or order)");
            into.Add(new JpxPoc(resStart, compStart, layerEnd, resEnd, compEnd, prog));
        }
    }

    bool Finish(Dictionary<int, TileState> tiles)
    {
        foreach (var (idx, ts) in tiles)
        {
            try { DecodeTile(idx, ts); }
            catch (PackedHeaderException e)
            {
                Warning = $"JPX: tile {idx} {e.Message}";
                return false;
            }
            catch (InvalidDataException e) when (e.Message.StartsWith("JPX: malformed POC", StringComparison.Ordinal))
            {
                Warning = e.Message;
                return false;
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException or NotSupportedException)
            {
                Warning ??= $"JPX: tile {idx} decode error ({e.Message})";
            }
        }
        return true;
    }

    void ParseSiz(int p)
    {
        Xsiz = U32(p + 2); Ysiz = U32(p + 6); XOsiz = U32(p + 10); YOsiz = U32(p + 14);
        XTsiz = U32(p + 18); YTsiz = U32(p + 22); XTOsiz = U32(p + 26); YTOsiz = U32(p + 30);
        Csiz = U16(p + 34);
        if (Xsiz <= 0 || Ysiz <= 0 || XTsiz <= 0 || YTsiz <= 0 || Csiz is < 1 or > 16384) throw new InvalidDataException("bad SIZ");
        Depth = new int[Csiz]; Xr = new int[Csiz]; Yr = new int[Csiz]; Signed = new bool[Csiz];
        for (var c = 0; c < Csiz; c++)
        {
            var s = _d[p + 36 + c * 3];
            Signed[c] = (s & 0x80) != 0; Depth[c] = (s & 0x7F) + 1;
            Xr[c] = Math.Max(1, (int)_d[p + 37 + c * 3]); Yr[c] = Math.Max(1, (int)_d[p + 38 + c * 3]);
        }
    }

    static JpxCodParams ParseSp(byte[] d, ref int p, bool precincts)
    {
        var cp = new JpxCodParams { NL = d[p++] };
        if (cp.NL > 32) throw new InvalidDataException("bad decomposition levels");
        cp.Xcb = (d[p++] & 15) + 2; cp.Ycb = (d[p++] & 15) + 2; cp.Style = d[p++]; cp.Transform = d[p++];
        cp.PPx = new int[cp.NL + 1]; cp.PPy = new int[cp.NL + 1];
        for (var i = 0; i <= cp.NL; i++)
        {
            if (precincts) { var b = d[p++]; cp.PPx[i] = b & 15; cp.PPy[i] = b >> 4; }
            else cp.PPx[i] = cp.PPy[i] = 15;
        }
        return cp;
    }

    void ParseCod(int p, int len, out int prog, out int layers, out int mct, out bool sop, out bool eph, out JpxCodParams cod)
    {
        var scod = _d[p++];
        sop = (scod & 2) != 0; eph = (scod & 4) != 0;
        prog = _d[p++]; layers = U16(p); p += 2; mct = _d[p++];
        cod = ParseSp(_d, ref p, (scod & 1) != 0);
    }

    void ParseCoc(int p, int len, Dictionary<int, JpxCodParams> into)
    {
        int c;
        if (Csiz < 257) c = _d[p++]; else { c = U16(p); p += 2; }
        var scoc = _d[p++];
        into[c] = ParseSp(_d, ref p, (scoc & 1) != 0);
    }

    JpxQuant ParseQuant(int p, int len)
    {
        var q = new JpxQuant();
        var s = _d[p++]; len--;
        q.Style = s & 31; q.Guard = s >> 5;
        if (q.Style == 0)
        {
            q.Exp = new int[len]; q.Mant = new int[len];
            for (var i = 0; i < len; i++) q.Exp[i] = _d[p + i] >> 3;
        }
        else
        {
            var n = q.Style == 1 ? 1 : len / 2;
            q.Exp = new int[n]; q.Mant = new int[n];
            for (var i = 0; i < n; i++) { var v = U16(p + 2 * i); q.Exp[i] = v >> 11; q.Mant[i] = v & 0x7FF; }
        }
        return q;
    }

    void ParseQcc(int p, int len, Dictionary<int, JpxQuant> into)
    {
        int c;
        if (Csiz < 257) { c = _d[p++]; len--; } else { c = U16(p); p += 2; len -= 2; }
        into[c] = ParseQuant(p, len);
    }

    void ParseRgn(int p, Dictionary<int, int> into)
    {
        int c;
        if (Csiz < 257) c = _d[p++]; else { c = U16(p); p += 2; }
        p++;
        into[c] = _d[p];
    }

    sealed class Pending(JpxCodeBlock cb, JpxSeg seg, int len)
    {
        public readonly JpxCodeBlock Cb = cb;
        public readonly JpxSeg Seg = seg;
        public readonly int Len = len;
    }

    readonly record struct PacketRef(int L, int C, int R, int P, long X, long Y);

    void DecodeTile(int idx, TileState ts)
    {
        var tp = ts.Cod is not null ? ts : null;
        var prog = tp is null ? _prog : ts.Prog;
        var layers = tp is null ? _layers : ts.Layers;
        var mct = tp is null ? _mct : ts.Mct;
        var tpIdx = idx % _ntx; var tqIdx = idx / _ntx;
        var tx0 = Math.Max(XTOsiz + tpIdx * XTsiz, XOsiz); var tx1 = (int)Math.Min((long)XTOsiz + (long)(tpIdx + 1) * XTsiz, Xsiz);
        var ty0 = Math.Max(YTOsiz + tqIdx * YTsiz, YOsiz); var ty1 = (int)Math.Min((long)YTOsiz + (long)(tqIdx + 1) * YTsiz, Ysiz);

        var comps = new TComp[Csiz];
        var maxR = 0;
        for (var c = 0; c < Csiz; c++)
        {
            var tc = new TComp
            {
                X0 = CeilDiv(tx0, Xr[c]), Y0 = CeilDiv(ty0, Yr[c]), X1 = CeilDiv(tx1, Xr[c]), Y1 = CeilDiv(ty1, Yr[c]),
                Cod = (ts.Coc.GetValueOrDefault(c) ?? ts.Cod ?? _coc.GetValueOrDefault(c) ?? _cod)!,
                Q = (ts.Qcc.GetValueOrDefault(c) ?? ts.Qcd ?? _qcc.GetValueOrDefault(c) ?? _qcd)!,
            };
            tc.Roi = ts.Roi.TryGetValue(c, out var rr) ? rr : _roi.GetValueOrDefault(c);
            BuildComp(tc, Depth[c]);
            maxR = Math.Max(maxR, tc.Cod.NL + 1);
            comps[c] = tc;
        }

        var pocs = ts.HasPoc ? ts.Pocs : _pocs;
        var order = pocs.Count > 0
            ? BuildOrder(comps, prog, layers, maxR, tx0, ty0, pocs)
            : BuildOrder(comps, prog, layers, maxR, tx0, ty0);
        var data = ts.Data.GetBuffer();
        var end = (int)ts.Data.Length;
        var pos = 0;
        var pend = new List<Pending>();
        if (ts.Parts.Exists(part => part.PacketHeaders is not null))
        {
            var packetIndex = 0;
            foreach (var part in ts.Parts)
            {
                var partPos = part.Start;
                var partEnd = part.Start + part.Length;
                var headerPos = 0;
                var headers = part.PacketHeaders;
                if (headers is null)
                {
                    while (partPos < partEnd && packetIndex < order.Count)
                    {
                        var pk = order[packetIndex++];
                        var tc = comps[pk.C];
                        var res = tc.Res[pk.R];
                        ReadPacket(_d, ref partPos, partEnd, null, ref headerPos, res, res.Prec[pk.P], pk.L, tc.Cod.Style, pend);
                    }
                    continue;
                }
                while (headerPos < headers.Length && packetIndex < order.Count)
                {
                    var pk = order[packetIndex++];
                    var tc = comps[pk.C];
                    var res = tc.Res[pk.R];
                    try { ReadPacket(_d, ref partPos, partEnd, headers, ref headerPos, res, res.Prec[pk.P], pk.L, tc.Cod.Style, pend); }
                    catch (IndexOutOfRangeException) { throw new PackedHeaderException("contains a truncated packet header"); }
                }
                if (headerPos < headers.Length) throw new PackedHeaderException("has more packet headers than the tile packet order");
                if (partPos < partEnd) throw new PackedHeaderException("has unreferenced tile-part packet data");
            }
            if (packetIndex < order.Count) throw new PackedHeaderException("has missing packet headers");
        }
        else
        {
            try
            {
                var headerPos = 0;
                foreach (var pk in order)
                {
                    if (pos >= end) break;
                    var tc = comps[pk.C];
                    var res = tc.Res[pk.R];
                    ReadPacket(data, ref pos, end, null, ref headerPos, res, res.Prec[pk.P], pk.L, tc.Cod.Style, pend);
                }
            }
            catch (IndexOutOfRangeException) { Warning ??= "JPX: truncated tile data"; }
        }

        DecodeBlocks(comps);
        for (var c = 0; c < Csiz; c++) comps[c].Out = Synthesize(comps[c]);

        if (mct != 0 && Csiz >= 3)
        {
            float[] a = comps[0].Out, b = comps[1].Out, cc = comps[2].Out;
            if (a.Length == b.Length && a.Length == cc.Length)
            {
                if (comps[0].Cod.Transform == 1)
                    for (var i = 0; i < a.Length; i++)
                    {
                        var g = a[i] - MathF.Floor((cc[i] + b[i]) * 0.25f);
                        var r = cc[i] + g; var bl = b[i] + g;
                        a[i] = r; b[i] = g; cc[i] = bl;
                    }
                else
                    for (var i = 0; i < a.Length; i++)
                    {
                        float y = a[i], cb = b[i], cr = cc[i];
                        a[i] = y + 1.402f * cr;
                        b[i] = y - 0.34413f * cb - 0.71414f * cr;
                        cc[i] = y + 1.772f * cb;
                    }
            }
        }

        for (var c = 0; c < Csiz; c++)
        {
            var tc = comps[c];
            var w = tc.X1 - tc.X0; var h = tc.Y1 - tc.Y0;
            if (w <= 0 || h <= 0) continue;
            var depth = Depth[c];
            var shift = Math.Max(0, depth - 16);
            var eff = depth - shift;
            int offset = Signed[c] ? 0 : 1 << (depth - 1), lo = Signed[c] ? -(1 << (depth - 1)) : 0, hi = Signed[c] ? (1 << (depth - 1)) - 1 : (1 << depth) - 1;
            var bias = Signed[c] ? 1 << (eff - 1) : 0;
            var ox = tc.X0 - CeilDiv(XOsiz, Xr[c]); var oy = tc.Y0 - CeilDiv(YOsiz, Yr[c]);
            var src = tc.Out; var dst = Planes[c]; var cw = CW[c];
            for (var y = 0; y < h; y++)
            {
                var so = y * w; var dof = (oy + y) * cw + ox;
                for (var x = 0; x < w; x++)
                {
                    var v = (int)MathF.Floor(src[so + x] + 0.5f) + offset;
                    v = v < lo ? lo : v > hi ? hi : v;
                    dst[dof + x] = (ushort)((v >> shift) + bias);
                }
            }
            tc.Out = [];
        }
    }

    void BuildComp(TComp tc, int depth)
    {
        var cp = tc.Cod; var q = tc.Q;
        var nl = cp.NL;
        tc.Res = new TRes[nl + 1];
        var rev = cp.Transform == 1;
        for (var r = 0; r <= nl; r++)
        {
            var n = nl - r;
            var res = new TRes
            {
                X0 = CeilShr(tc.X0, n), Y0 = CeilShr(tc.Y0, n), X1 = CeilShr(tc.X1, n), Y1 = CeilShr(tc.Y1, n),
                PPx = cp.PPx[r], PPy = cp.PPy[r],
            };
            res.Npw = res.X1 > res.X0 ? CeilShr(res.X1, res.PPx) - (res.X0 >> res.PPx) : 0;
            res.Nph = res.Y1 > res.Y0 ? CeilShr(res.Y1, res.PPy) - (res.Y0 >> res.PPy) : 0;
            var nb = r == 0 ? 1 : 3;
            res.Bands = new TBand[nb];
            for (var bi = 0; bi < nb; bi++)
            {
                var orient = r == 0 ? 0 : bi + 1;
                var lev = r == 0 ? nl : nl - r + 1;
                var band = new TBand { Orient = orient };
                int xo = orient & 1, yo = orient >> 1;
                if (r == 0)
                {
                    band.X0 = res.X0; band.Y0 = res.Y0; band.X1 = res.X1; band.Y1 = res.Y1;
                }
                else
                {
                    var half = 1L << (lev - 1);
                    band.X0 = (int)(((long)tc.X0 - half * xo + (1L << lev) - 1) >> lev);
                    band.X1 = (int)(((long)tc.X1 - half * xo + (1L << lev) - 1) >> lev);
                    band.Y0 = (int)(((long)tc.Y0 - half * yo + (1L << lev) - 1) >> lev);
                    band.Y1 = (int)(((long)tc.Y1 - half * yo + (1L << lev) - 1) >> lev);
                }
                int qi, eb, mant;
                if (q.Style == 1)
                {
                    eb = q.Exp[0] - (r == 0 ? 0 : r - 1); mant = q.Mant[0];
                }
                else
                {
                    qi = r == 0 ? 0 : 3 * (r - 1) + orient;
                    if (qi >= q.Exp.Length) qi = q.Exp.Length - 1;
                    eb = q.Exp[qi]; mant = q.Mant[qi];
                }
                band.Mb = q.Guard + eb - 1;
                if (q.Style == 0 || rev && q.Style == 0) band.Step = 1f;
                else
                {
                    var gain = orient == 0 ? 0 : orient == 3 ? 2 : 1;
                    band.Step = (float)((1.0 + mant / 2048.0) * Math.Pow(2, depth + gain - eb));
                }
                var bw = Math.Max(0, band.X1 - band.X0); var bh = Math.Max(0, band.Y1 - band.Y0);
                band.Coef = new float[(long)bw * bh];
                res.Bands[bi] = band;
            }

            var total = res.Npw * res.Nph;
            res.Prec = new TPrec[total];
            var cbShiftX = Math.Min(cp.Xcb, r == 0 ? res.PPx : res.PPx - 1);
            var cbShiftY = Math.Min(cp.Ycb, r == 0 ? res.PPy : res.PPy - 1);
            for (var py = 0; py < res.Nph; py++)
                for (var px = 0; px < res.Npw; px++)
                {
                    var prec = new TPrec { PB = new TPrecBand[nb] };
                    var prx0 = ((res.X0 >> res.PPx) + px) << res.PPx; var pry0 = ((res.Y0 >> res.PPy) + py) << res.PPy;
                    var prx1 = prx0 + (1 << res.PPx); var pry1 = pry0 + (1 << res.PPy);
                    for (var bi = 0; bi < nb; bi++)
                    {
                        var band = res.Bands[bi];
                        int bx0 = prx0, by0 = pry0, bx1 = prx1, by1 = pry1;
                        if (r > 0) { bx0 >>= 1; by0 >>= 1; bx1 >>= 1; by1 >>= 1; }
                        int cx0 = Math.Max(bx0, band.X0), cy0 = Math.Max(by0, band.Y0), cx1 = Math.Min(bx1, band.X1), cy1 = Math.Min(by1, band.Y1);
                        var pb = new TPrecBand();
                        if (cx1 > cx0 && cy1 > cy0)
                        {
                            var i0 = cx0 >> cbShiftX; var i1 = (cx1 - 1) >> cbShiftX;
                            var j0 = cy0 >> cbShiftY; var j1 = (cy1 - 1) >> cbShiftY;
                            pb.Ncw = i1 - i0 + 1; pb.Nch = j1 - j0 + 1;
                            pb.Blocks = new JpxCodeBlock[pb.Ncw * pb.Nch];
                            for (var j = 0; j < pb.Nch; j++)
                                for (var i = 0; i < pb.Ncw; i++)
                                    pb.Blocks[j * pb.Ncw + i] = new JpxCodeBlock
                                    {
                                        X0 = Math.Max((i0 + i) << cbShiftX, cx0), X1 = Math.Min((i0 + i + 1) << cbShiftX, cx1),
                                        Y0 = Math.Max((j0 + j) << cbShiftY, cy0), Y1 = Math.Min((j0 + j + 1) << cbShiftY, cy1),
                                    };
                            pb.Incl = new JpxTagTree(pb.Ncw, pb.Nch); pb.Zbp = new JpxTagTree(pb.Ncw, pb.Nch);
                        }
                        prec.PB[bi] = pb;
                    }
                    res.Prec[py * res.Npw + px] = prec;
                }
            tc.Res[r] = res;
        }
    }

    List<PacketRef> BuildOrder(TComp[] comps, int prog, int layers, int maxR, int tx0, int ty0, IReadOnlyList<JpxPoc> pocs)
    {
        var list = new List<PacketRef>();
        var emitted = new HashSet<(int L, int C, int R, int P)>();

        foreach (var poc in pocs)
        {
            if (poc.LayerEnd > layers || poc.ResStart >= maxR || poc.ResEnd > maxR ||
                poc.CompStart >= Csiz || poc.CompEnd > Csiz)
                throw new InvalidDataException("JPX: malformed POC marker (range exceeds coding parameters)");

            void Add(int l, int c, int r, int p, long x = 0, long y = 0)
            {
                if (emitted.Add((l, c, r, p))) list.Add(new PacketRef(l, c, r, p, x, y));
            }

            if (poc.Prog is 0 or 1)
            {
                if (poc.Prog == 0)
                    for (var l = 0; l < poc.LayerEnd; l++)
                        for (var r = poc.ResStart; r < poc.ResEnd; r++)
                            for (var c = poc.CompStart; c < poc.CompEnd; c++)
                                if (r < comps[c].Res.Length)
                                    for (var p = 0; p < comps[c].Res[r].Prec.Length; p++) Add(l, c, r, p);
                if (poc.Prog == 1)
                    for (var r = poc.ResStart; r < poc.ResEnd; r++)
                        for (var l = 0; l < poc.LayerEnd; l++)
                            for (var c = poc.CompStart; c < poc.CompEnd; c++)
                                if (r < comps[c].Res.Length)
                                    for (var p = 0; p < comps[c].Res[r].Prec.Length; p++) Add(l, c, r, p);
                continue;
            }

            var bases = new List<PacketRef>();
            for (var c = poc.CompStart; c < poc.CompEnd; c++)
            {
                var nl = comps[c].Cod.NL;
                for (var r = poc.ResStart; r < poc.ResEnd && r <= nl; r++)
                {
                    var res = comps[c].Res[r];
                    for (var py = 0; py < res.Nph; py++)
                        for (var px = 0; px < res.Npw; px++)
                        {
                            var gx = (long)(((res.X0 >> res.PPx) + px) << res.PPx) << (nl - r);
                            var gy = (long)(((res.Y0 >> res.PPy) + py) << res.PPy) << (nl - r);
                            gx = Math.Max(tx0, gx * Xr[c]); gy = Math.Max(ty0, gy * Yr[c]);
                            bases.Add(new PacketRef(0, c, r, py * res.Npw + px, gx, gy));
                        }
                }
            }
            Comparison<PacketRef> cmp = poc.Prog switch
            {
                2 => (a, b) => a.R != b.R ? a.R.CompareTo(b.R) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.C != b.C ? a.C.CompareTo(b.C) : a.P.CompareTo(b.P),
                3 => (a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.C != b.C ? a.C.CompareTo(b.C) : a.R != b.R ? a.R.CompareTo(b.R) : a.P.CompareTo(b.P),
                _ => (a, b) => a.C != b.C ? a.C.CompareTo(b.C) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.R != b.R ? a.R.CompareTo(b.R) : a.P.CompareTo(b.P),
            };
            bases.Sort(cmp);
            foreach (var b in bases)
                for (var l = 0; l < poc.LayerEnd; l++) Add(l, b.C, b.R, b.P, b.X, b.Y);
        }

        long expected = 0;
        foreach (var comp in comps)
            foreach (var res in comp.Res)
                expected += (long)layers * res.Prec.Length;
        if (list.Count != expected)
            throw new InvalidDataException("JPX: malformed POC marker (progression ranges do not cover every packet)");
        return list;
    }

    List<PacketRef> BuildOrder(TComp[] comps, int prog, int layers, int maxR, int tx0, int ty0)
    {
        var list = new List<PacketRef>();
        if (prog == 0)
        {
            for (var l = 0; l < layers; l++)
                for (var r = 0; r < maxR; r++)
                    for (var c = 0; c < Csiz; c++)
                        if (r < comps[c].Res.Length)
                            for (var p = 0; p < comps[c].Res[r].Prec.Length; p++) list.Add(new PacketRef(l, c, r, p, 0, 0));
            return list;
        }
        if (prog == 1)
        {
            for (var r = 0; r < maxR; r++)
                for (var l = 0; l < layers; l++)
                    for (var c = 0; c < Csiz; c++)
                        if (r < comps[c].Res.Length)
                            for (var p = 0; p < comps[c].Res[r].Prec.Length; p++) list.Add(new PacketRef(l, c, r, p, 0, 0));
            return list;
        }
        var bases = new List<PacketRef>();
        for (var c = 0; c < Csiz; c++)
        {
            var nl = comps[c].Cod.NL;
            for (var r = 0; r <= nl; r++)
            {
                var res = comps[c].Res[r];
                for (var py = 0; py < res.Nph; py++)
                    for (var px = 0; px < res.Npw; px++)
                    {
                        var gx = (long)(((res.X0 >> res.PPx) + px) << res.PPx) << (nl - r) ;
                        var gy = (long)(((res.Y0 >> res.PPy) + py) << res.PPy) << (nl - r);
                        gx = Math.Max(tx0, gx * Xr[c]); gy = Math.Max(ty0, gy * Yr[c]);
                        bases.Add(new PacketRef(0, c, r, py * res.Npw + px, gx, gy));
                    }
            }
        }
        Comparison<PacketRef> cmp = prog switch
        {
            2 => (a, b) => a.R != b.R ? a.R.CompareTo(b.R) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.C != b.C ? a.C.CompareTo(b.C) : a.P.CompareTo(b.P),
            3 => (a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.C != b.C ? a.C.CompareTo(b.C) : a.R != b.R ? a.R.CompareTo(b.R) : a.P.CompareTo(b.P),
            _ => (a, b) => a.C != b.C ? a.C.CompareTo(b.C) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.R != b.R ? a.R.CompareTo(b.R) : a.P.CompareTo(b.P),
        };
        bases.Sort(cmp);
        foreach (var b in bases)
            for (var l = 0; l < layers; l++) list.Add(b with { L = l });
        return list;
    }

    static JpxSeg NewSeg(JpxCodeBlock cb, int style)
    {
        var s = new JpxSeg();
        if ((style & 4) != 0) s.MaxPasses = 1;
        else if ((style & 1) != 0)
        {
            if (cb.Segs.Count == 0) s.MaxPasses = 10;
            else { var prev = cb.Segs[^1].MaxPasses; s.MaxPasses = prev is 1 or 10 ? 2 : 1; }
        }
        else s.MaxPasses = 10000;
        cb.Segs.Add(s);
        return s;
    }

    static int Log2(int n) => 31 - System.Numerics.BitOperations.LeadingZeroCount((uint)n);

    void ReadPacket(byte[] d, ref int pos, int end, byte[]? packedHeaders, ref int headerPos, TRes res, TPrec prec, int layer, int style, List<Pending> pend)
    {
        if (pos + 6 <= end && d[pos] == 0xFF && d[pos + 1] == 0x91) pos += 6;
        var packetHeaderData = packedHeaders ?? d;
        var packetHeaderPos = packedHeaders is null ? pos : headerPos;
        var packetHeaderEnd = packedHeaders?.Length ?? end;
        var br = new JpxBits(packetHeaderData, packetHeaderPos, packetHeaderEnd);
        pend.Clear();
        if (br.Bit() != 0)
        {
            for (var bi = 0; bi < res.Bands.Length; bi++)
            {
                var pb = prec.PB[bi];
                for (var j = 0; j < pb.Blocks.Length; j++)
                {
                    var cb = pb.Blocks[j];
                    int cx = j % pb.Ncw, cy = j / pb.Ncw;
                    bool incl;
                    if (!cb.Included) incl = pb.Incl!.Decode(br, cx, cy, layer + 1);
                    else incl = br.Bit() == 1;
                    if (!incl) continue;
                    if (!cb.Included)
                    {
                        var i = 1;
                        while (!pb.Zbp!.Decode(br, cx, cy, i)) { i++; if (br.Overrun || i > 80) throw new IndexOutOfRangeException(); }
                        cb.Zbp = i - 1; cb.Included = true; cb.LBlock = 3;
                    }
                    int np;
                    if (br.Bit() == 0) np = 1;
                    else if (br.Bit() == 0) np = 2;
                    else
                    {
                        var v = br.Bits(2);
                        if (v != 3) np = 3 + v;
                        else
                        {
                            v = br.Bits(5);
                            np = v != 31 ? 6 + v : 37 + br.Bits(7);
                        }
                    }
                    while (br.Bit() == 1) { cb.LBlock++; if (cb.LBlock > 40 || br.Overrun) throw new IndexOutOfRangeException(); }
                    var seg = cb.Segs.Count > 0 ? cb.Segs[^1] : null;
                    if (seg is null || seg.Passes >= seg.MaxPasses) seg = NewSeg(cb, style);
                    var remaining = np;
                    while (true)
                    {
                        var n = Math.Min(seg.MaxPasses - seg.Passes, remaining);
                        var len = br.Bits(cb.LBlock + Log2(n));
                        pend.Add(new Pending(cb, seg, len));
                        seg.Passes += n; remaining -= n;
                        if (remaining <= 0) break;
                        seg = NewSeg(cb, style);
                    }
                    cb.NumPasses += np;
                }
            }
        }
        if (br.Overrun)
        {
            if (packedHeaders is not null) throw new PackedHeaderException("contains a truncated packet header");
            throw new IndexOutOfRangeException();
        }
        var alignedPos = br.Align();
        if (packedHeaders is null)
        {
            pos = alignedPos;
            if (pos + 2 <= end && d[pos] == 0xFF && d[pos + 1] == 0x92) pos += 2;
        }
        else
        {
            headerPos = alignedPos;
            if (headerPos + 2 <= packetHeaderEnd && packetHeaderData[headerPos] == 0xFF && packetHeaderData[headerPos + 1] == 0x92) headerPos += 2;
        }
        foreach (var pd in pend)
        {
            if (packedHeaders is not null && pd.Len > end - pos) throw new PackedHeaderException("contains truncated packet data");
            var n = Math.Min(pd.Len, end - pos);
            if (n < 0) throw new IndexOutOfRangeException();
            pd.Cb.Append(d.AsSpan(pos, n));
            pd.Seg.Len += n;
            pos += pd.Len;
        }
    }

    readonly struct Job(JpxCodeBlock cb, TBand band, int style, int roi, int numbps, bool rev)
    {
        public readonly JpxCodeBlock Cb = cb;
        public readonly TBand Band = band;
        public readonly int Style = style, Roi = roi, Numbps = numbps;
        public readonly bool Rev = rev;
    }

    static void DecodeBlocks(TComp[] comps)
    {
        var jobs = new List<Job>();
        foreach (var tc in comps)
            foreach (var res in tc.Res)
                foreach (var prec in res.Prec)
                    for (var bi = 0; bi < res.Bands.Length; bi++)
                        foreach (var cb in prec.PB[bi].Blocks)
                        {
                            if (cb.NumPasses == 0) continue;
                            var nb = res.Bands[bi].Mb + tc.Roi - cb.Zbp;
                            if (nb <= 0 || nb > 30) continue;
                            jobs.Add(new Job(cb, res.Bands[bi], tc.Cod.Style, tc.Roi, nb, tc.Cod.Transform == 1 && tc.Q.Style == 0));
                        }
        var arr = jobs.ToArray();
        if (arr.Length < 8)
        {
            var t1 = new JpxTier1();
            foreach (var j in arr) RunJob(t1, j);
            return;
        }
        Parallel.For(0, arr.Length, () => new JpxTier1(), (i, _, t1) => { RunJob(t1, arr[i]); return t1; }, _ => { });
    }

    static void RunJob(JpxTier1 t1, Job j)
    {
        var cb = j.Cb; var band = j.Band;
        int w = cb.X1 - cb.X0, h = cb.Y1 - cb.Y0;
        t1.Decode(cb, w, h, band.Orient, j.Numbps, j.Style);
        var data = t1.Data;
        var bw = band.X1 - band.X0;
        var thr = j.Roi > 0 ? 1 << (j.Roi + 1) : int.MaxValue;
        var step = band.Step * 0.5f;
        for (var y = 0; y < h; y++)
        {
            var dst = (cb.Y0 - band.Y0 + y) * bw + cb.X0 - band.X0;
            for (var x = 0; x < w; x++)
            {
                var v = data[y * w + x];
                if (v == 0) continue;
                if (j.Roi > 0) { var a = Math.Abs(v); if (a >= thr) v = v < 0 ? -(a >> j.Roi) : a >> j.Roi; }
                band.Coef[dst + x] = j.Rev ? v / 2 : v * step;
            }
        }
    }

    // ---------- inverse DWT ----------

    const int Ext = 4;
    const float Alpha = -1.586134342059924f, Beta = -0.052980118572961f, Gamma = 0.882911075530934f, Delta = 0.443506852043971f, K = 1.230174104914001f;

    static float[] Synthesize(TComp tc)
    {
        var cp = tc.Cod;
        var rev = cp.Transform == 1;
        var cur = tc.Res[0].Bands[0].Coef;
        for (var r = 1; r <= cp.NL; r++)
        {
            var res = tc.Res[r];
            int w = res.X1 - res.X0, h = res.Y1 - res.Y0;
            if (w <= 0 || h <= 0) { cur = []; continue; }
            var o = new float[(long)w * h];
            var hl = res.Bands[0]; var lh = res.Bands[1]; var hh = res.Bands[2];
            var lw = ((res.X1 + 1) >> 1) - ((res.X0 + 1) >> 1);
            var hw = (res.X1 >> 1) - (res.X0 >> 1);
            var lowStart = (res.X0 + 1) >> 1; var highStart = res.X0 >> 1;
            for (var y = 0; y < h; y++)
            {
                var ya = res.Y0 + y;
                var odd = (ya & 1) != 0;
                var ly = odd ? (ya >> 1) - (res.Y0 >> 1) : (ya >> 1) - ((res.Y0 + 1) >> 1);
                var low = odd ? lh.Coef : cur;
                var high = odd ? hh.Coef : hl.Coef;
                var lrow = ly * lw; var hrow = ly * hw;
                var dst = y * w;
                for (var x = 0; x < w; x++)
                {
                    var xa = res.X0 + x;
                    o[dst + x] = (xa & 1) == 0 ? low[lrow + (xa >> 1) - lowStart] : high[hrow + (xa >> 1) - highStart];
                }
            }
            Horizontal(o, w, h, res.X0 & 1, rev);
            Vertical(o, w, h, res.Y0 & 1, rev);
            cur = o;
            foreach (var b in res.Bands) b.Coef = [];
        }
        tc.Res[0].Bands[0].Coef = [];
        return cur;
    }

    static void Horizontal(float[] a, int w, int h, int parity, bool rev)
    {
        if (w == 1)
        {
            if (parity == 1) for (var y = 0; y < h; y++) a[y] *= 0.5f;
            return;
        }
        var buf = new float[w + 2 * Ext];
        for (var y = 0; y < h; y++)
        {
            Array.Copy(a, y * w, buf, Ext, w);
            Lift(buf, w, 1, parity, rev);
            Array.Copy(buf, Ext, a, y * w, w);
        }
    }

    static void Vertical(float[] a, int w, int h, int parity, bool rev)
    {
        if (h == 1)
        {
            if (parity == 1) for (var x = 0; x < w; x++) a[x] *= 0.5f;
            return;
        }
        const int Lanes = 16;
        var buf = new float[(h + 2 * Ext) * Lanes];
        for (var x0 = 0; x0 < w; x0 += Lanes)
        {
            var n = Math.Min(Lanes, w - x0);
            for (var y = 0; y < h; y++) Array.Copy(a, y * w + x0, buf, (y + Ext) * Lanes, n);
            Lift(buf, h, Lanes, parity, rev);
            for (var y = 0; y < h; y++) Array.Copy(buf, (y + Ext) * Lanes, a, y * w + x0, n);
        }
    }

    static int Refl(int i, int n)
    {
        if (n == 1) return 0;
        var period = 2 * (n - 1);
        i = Math.Abs(i) % period;
        return i >= n ? period - i : i;
    }

    static void Lift(float[] b, int n, int lanes, int parity, bool rev)
    {
        for (var k = 1; k <= Ext; k++)
        {
            Array.Copy(b, (Refl(-k, n) + Ext) * lanes, b, (Ext - k) * lanes, lanes);
            Array.Copy(b, (Refl(n - 1 + k, n) + Ext) * lanes, b, (Ext + n - 1 + k) * lanes, lanes);
        }
        if (rev)
        {
            Step53(b, n, lanes, parity, -1, n + 1, 0, true);
            Step53(b, n, lanes, parity, 0, n, 1, false);
            return;
        }
        for (var j = -Ext; j < n + Ext; j++)
        {
            var f = ((j + parity) & 1) == 0 ? K : 1f / K;
            var o = (j + Ext) * lanes;
            for (var l = 0; l < lanes; l++) b[o + l] *= f;
        }
        Step97(b, lanes, parity, -3, n + 3, 0, Delta);
        Step97(b, lanes, parity, -2, n + 2, 1, Gamma);
        Step97(b, lanes, parity, -1, n + 1, 0, Beta);
        Step97(b, lanes, parity, 0, n, 1, Alpha);
    }

    static void Step97(float[] b, int lanes, int parity, int lo, int hi, int want, float coef)
    {
        var j = lo;
        if (((j + parity) & 1) != want) j++;
        for (; j < hi; j += 2)
        {
            int o = (j + Ext) * lanes, p = o - lanes, q = o + lanes;
            for (var l = 0; l < lanes; l++) b[o + l] -= coef * (b[p + l] + b[q + l]);
        }
    }

    static void Step53(float[] b, int n, int lanes, int parity, int lo, int hi, int want, bool even)
    {
        var j = lo;
        if (((j + parity) & 1) != want) j++;
        for (; j < hi; j += 2)
        {
            int o = (j + Ext) * lanes, p = o - lanes, q = o + lanes;
            if (even) for (var l = 0; l < lanes; l++) b[o + l] -= MathF.Floor((b[p + l] + b[q + l] + 2f) * 0.25f);
            else for (var l = 0; l < lanes; l++) b[o + l] += MathF.Floor((b[p + l] + b[q + l]) * 0.5f);
        }
    }
}
