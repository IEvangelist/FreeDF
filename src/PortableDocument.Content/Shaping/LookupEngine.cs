namespace PortableDocument.Content.Shaping;

/// <summary>
/// Applies GSUB lookup types 1-8 and GPOS lookup types 1-9 to a <see cref="GlyphBuffer"/> held in logical order.
/// Lookup flags (ignore base/ligature/mark, mark attachment type, mark filtering sets), extension subtables,
/// nested contextual lookups and per-glyph feature masks are honoured.
/// </summary>
internal sealed class LookupEngine(OpenTypeLayout layout, GlyphBuffer buf, bool rtl, int alternateIndex = 0)
{
    readonly byte[] _d = layout.D;
    int _flags, _markSet, _mask, _depth;
    bool _gpos;
    int _next;
    const int MaxDepth = 6;

    int U16(int o) => Ot.U16(_d, o);
    int S16(int o) => Ot.S16(_d, o);

    public void RunStage(bool gpos, PlanLookup[]? lookups)
    {
        if (lookups is null) return;
        foreach (var l in lookups) RunLookup(gpos, l.Index, l.Mask);
    }

    void RunLookup(bool gpos, int index, int mask)
    {
        var lk = layout.LookupOffset(gpos, index);
        if (lk < 0) return;
        _gpos = gpos; _mask = mask;
        var type = U16(lk);
        SetFlags(lk);
        var reverse = !gpos && type == 8 || !gpos && type == 7 && ResolveType(lk, 0, out _) == 8;
        if (reverse)
        {
            for (var i = buf.Len - 1; i >= 0; i--)
                if ((buf.Info[i].Mask & mask) != 0 && !Skip(i)) ApplyAt(lk, i);
            return;
        }
        var pos = 0;
        while (pos < buf.Len)
        {
            if ((buf.Info[pos].Mask & mask) != 0 && !Skip(pos) && ApplyAt(lk, pos)) pos = _next > pos ? _next : pos + (buf.Len > 0 && _next == pos ? 0 : 1);
            else pos++;
        }
    }

    void SetFlags(int lk)
    {
        _flags = U16(lk + 2);
        _markSet = (_flags & 0x10) != 0 ? U16(lk + 6 + U16(lk + 4) * 2) : 0;
    }

    int ResolveType(int lk, int sub, out int subOff)
    {
        var type = U16(lk);
        subOff = lk + U16(lk + 6 + sub * 2);
        var extension = _gpos ? 9 : 7;
        if (type == extension)
        {
            var real = U16(subOff + 2);
            subOff += (int)Ot.U32(_d, subOff + 4);
            return real;
        }
        return type;
    }

    bool Skip(int i)
    {
        var g = buf.Info[i];
        switch (g.Class)
        {
            case 1 when (_flags & 2) != 0:
            case 2 when (_flags & 4) != 0:
                return true;
            case 3:
                if ((_flags & 8) != 0) return true;
                if ((_flags & 0x10) != 0 && !layout.InMarkSet(_markSet, g.Gid)) return true;
                var mt = _flags >> 8;
                if (mt != 0 && layout.MarkAttachClass(g.Gid) != mt) return true;
                break;
        }
        return false;
    }

    int NextIdx(int i) { for (i++; i < buf.Len; i++) if (!Skip(i)) return i; return -1; }
    int PrevIdx(int i) { for (i--; i >= 0; i--) if (!Skip(i)) return i; return -1; }

    bool ApplyAt(int lk, int pos)
    {
        var n = U16(lk + 4);
        for (var s = 0; s < n; s++)
        {
            var type = ResolveType(lk, s, out var sub);
            _next = pos + 1;
            if (_gpos ? ApplyGpos(type, sub, pos) : ApplyGsub(type, sub, pos)) return true;
        }
        return false;
    }

    // ---------- GSUB ----------
    bool ApplyGsub(int type, int sub, int pos)
    {
        var gid = buf.Info[pos].Gid;
        var fmt = U16(sub);
        switch (type)
        {
            case 1:
            {
                var ci = Ot.Coverage(_d, sub + U16(sub + 2), gid);
                if (ci < 0) return false;
                var ng = fmt == 1 ? (gid + S16(sub + 4)) & 0xFFFF : U16(sub + 6 + ci * 2);
                SetGlyph(pos, ng);
                return true;
            }
            case 2:
            {
                var ci = Ot.Coverage(_d, sub + U16(sub + 2), gid);
                if (ci < 0 || ci >= U16(sub + 4)) return false;
                var seq = sub + U16(sub + 6 + ci * 2);
                var cnt = U16(seq);
                Span<int> gids = cnt <= 32 ? stackalloc int[cnt] : new int[cnt];
                for (var k = 0; k < cnt; k++) gids[k] = U16(seq + 2 + k * 2);
                if (cnt == 0)
                {
                    if (buf.Len == 1) return false;
                    var gi = buf.Info[pos];
                    buf.RemoveAt(pos);
                    var t = Math.Min(pos, buf.Len - 1); if (pos > 0) t = pos - 1;
                    buf.Info[t].Start = Math.Min(buf.Info[t].Start, gi.Start); buf.Info[t].End = Math.Max(buf.Info[t].End, gi.End);
                    _next = Math.Max(pos, 0);
                    return true;
                }
                buf.Replace(pos, 1, gids, layout);
                _next = pos + cnt;
                return true;
            }
            case 3:
            {
                var ci = Ot.Coverage(_d, sub + U16(sub + 2), gid);
                if (ci < 0 || ci >= U16(sub + 4)) return false;
                var set = sub + U16(sub + 6 + ci * 2);
                var cnt = U16(set);
                if (cnt == 0) return false;
                SetGlyph(pos, U16(set + 2 + Math.Min(alternateIndex, cnt - 1) * 2));
                return true;
            }
            case 4: return Ligature(sub, pos, gid);
            case 5: return Context(sub, pos, chain: false);
            case 6: return Context(sub, pos, chain: true);
            case 8: return ReverseChain(sub, pos, gid);
        }
        return false;
    }

    void SetGlyph(int pos, int gid)
    {
        buf.Info[pos].Gid = gid;
        buf.Info[pos].Class = layout.GlyphClass(gid);
    }

    bool Ligature(int sub, int pos, int gid)
    {
        var ci = Ot.Coverage(_d, sub + U16(sub + 2), gid);
        if (ci < 0 || ci >= U16(sub + 4)) return false;
        var set = sub + U16(sub + 6 + ci * 2);
        var count = U16(set);
        for (var l = 0; l < count; l++)
        {
            var lig = set + U16(set + 2 + l * 2);
            var ligGlyph = U16(lig);
            var comps = U16(lig + 2);
            if (comps == 0) continue;
            var matched = new int[comps];
            matched[0] = pos;
            var p = pos; var ok = true;
            for (var k = 1; k < comps; k++)
            {
                p = NextIdx(p);
                if (p < 0 || buf.Info[p].Gid != U16(lig + 4 + (k - 1) * 2) || (buf.Info[p].Mask & _mask) == 0) { ok = false; break; }
                matched[k] = p;
            }
            if (!ok) continue;
            ApplyLigature(matched, ligGlyph);
            return true;
        }
        return false;
    }

    void ApplyLigature(int[] matched, int ligGlyph)
    {
        var first = matched[0]; var last = matched[^1];
        var id = buf.NewLigId();
        var start = int.MaxValue; var end = 0; var flags = 0;
        foreach (var m in matched) { start = Math.Min(start, buf.Info[m].Start); end = Math.Max(end, buf.Info[m].End); flags |= buf.Info[m].Flags; }
        var keep = new List<(GlyphInfo I, GlyphPos P)>();
        var comp = 0;
        for (var i = first + 1; i <= last; i++)
        {
            if (comp + 1 < matched.Length && matched[comp + 1] == i) { comp++; continue; }
            var gi = buf.Info[i]; gi.LigId = id; gi.LigComp = comp + 1;
            keep.Add((gi, buf.Pos[i]));
        }
        var removed = last - first;
        var li = buf.Info[first];
        li.Gid = ligGlyph; li.Class = layout.GlyphClass(ligGlyph); li.Start = start; li.End = end; li.Flags = flags | GlyphInfo.FlagLigature;
        li.LigId = id; li.LigNum = matched.Length; li.LigComp = 0;
        if (li.Class == 0) li.Class = 2;
        buf.Info[first] = li;
        buf.Pos[first] = default;
        var delta = removed - keep.Count;
        if (delta > 0)
        {
            Array.Copy(buf.Info, last + 1, buf.Info, last + 1 - delta, buf.Len - last - 1);
            Array.Copy(buf.Pos, last + 1, buf.Pos, last + 1 - delta, buf.Len - last - 1);
            buf.Len -= delta;
        }
        for (var k = 0; k < keep.Count; k++) { buf.Info[first + 1 + k] = keep[k].I; buf.Pos[first + 1 + k] = keep[k].P; }
        _next = first + 1 + keep.Count;
        _lengthDelta = -delta;
    }

    int _lengthDelta;

    bool ReverseChain(int sub, int pos, int gid)
    {
        if (U16(sub) != 1) return false;
        var ci = Ot.Coverage(_d, sub + U16(sub + 2), gid);
        if (ci < 0) return false;
        var o = sub + 4;
        var bt = U16(o); o += 2;
        var btArr = o; o += bt * 2;
        var la = U16(o); o += 2;
        var laArr = o; o += la * 2;
        var gc = U16(o);
        if (ci >= gc) return false;
        var p = pos;
        for (var k = 0; k < bt; k++)
        {
            p = PrevIdx(p);
            if (p < 0 || Ot.Coverage(_d, sub + U16(btArr + k * 2), buf.Info[p].Gid) < 0) return false;
        }
        p = pos;
        for (var k = 0; k < la; k++)
        {
            p = NextIdx(p);
            if (p < 0 || Ot.Coverage(_d, sub + U16(laArr + k * 2), buf.Info[p].Gid) < 0) return false;
        }
        SetGlyph(pos, U16(o + 2 + ci * 2));
        return true;
    }

    // ---------- contexts (GSUB 5/6, GPOS 7/8) ----------
    // matching modes: 0 = glyph ids, 1 = classes, 2 = coverage tables
    bool MatchOne(int mode, int baseOff, int arr, int i, int gid) => mode switch
    {
        0 => U16(arr + i * 2) == gid,
        1 => Ot.Class(_d, baseOff, gid) == U16(arr + i * 2),
        _ => Ot.Coverage(_d, baseOff + U16(arr + i * 2), gid) >= 0,
    };

    bool Context(int sub, int pos, bool chain)
    {
        var fmt = U16(sub);
        var gid = buf.Info[pos].Gid;
        if (fmt == 1)
        {
            var ci = Ot.Coverage(_d, sub + U16(sub + 2), gid);
            if (ci < 0 || ci >= U16(sub + 4)) return false;
            var set = U16(sub + 6 + ci * 2);
            if (set == 0) return false;
            set += sub;
            var n = U16(set);
            for (var r = 0; r < n; r++)
                if (TryRule(set + U16(set + 2 + r * 2), pos, chain, 0, 0, 0, 0)) return true;
            return false;
        }
        if (fmt == 2)
        {
            if (Ot.Coverage(_d, sub + U16(sub + 2), gid) < 0) return false;
            if (!chain)
            {
                var cd = sub + U16(sub + 4);
                var cls = Ot.Class(_d, cd, gid);
                if (cls >= U16(sub + 6)) return false;
                var set = U16(sub + 8 + cls * 2);
                if (set == 0) return false;
                set += sub;
                var n = U16(set);
                for (var r = 0; r < n; r++)
                    if (TryRule(set + U16(set + 2 + r * 2), pos, false, 1, cd, 0, 0)) return true;
                return false;
            }
            else
            {
                var btCd = U16(sub + 4); var inCd = sub + U16(sub + 6); var laCd = U16(sub + 8);
                var cls = Ot.Class(_d, inCd, gid);
                if (cls >= U16(sub + 10)) return false;
                var set = U16(sub + 12 + cls * 2);
                if (set == 0) return false;
                set += sub;
                var n = U16(set);
                for (var r = 0; r < n; r++)
                    if (TryRule(set + U16(set + 2 + r * 2), pos, true, 1, inCd, btCd == 0 ? 0 : sub + btCd, laCd == 0 ? 0 : sub + laCd)) return true;
                return false;
            }
        }
        if (fmt == 3) return Format3(sub, pos, chain);
        return false;
    }

    // Formats 1/2 rule: [chain: backtrackCount, backtrack[]] inputCount, input[inputCount-1], [chain: lookaheadCount, lookahead[]], substCount, records
    bool TryRule(int rule, int pos, bool chain, int mode, int inCd, int btCd, int laCd)
    {
        var o = rule;
        int btCount = 0, btArr = 0, laCount = 0, laArr = 0;
        if (chain) { btCount = U16(o); btArr = o + 2; o += 2 + btCount * 2; }
        var inCount = U16(o); var inArr = o + 2;
        int substCount, records;
        if (chain)
        {
            o += 2 + Math.Max(inCount - 1, 0) * 2;
            laCount = U16(o); laArr = o + 2; o += 2 + laCount * 2;
            substCount = U16(o); records = o + 2;
        }
        else
        {
            substCount = U16(o + 2); inArr = o + 4; records = inArr + Math.Max(inCount - 1, 0) * 2;
        }
        if (inCount == 0) return false;

        var matched = new int[inCount];
        matched[0] = pos;
        var p = pos;
        for (var k = 1; k < inCount; k++)
        {
            p = NextIdx(p);
            if (p < 0 || (buf.Info[p].Mask & _mask) == 0 || !MatchOne(mode, inCd, inArr, k - 1, buf.Info[p].Gid)) return false;
            matched[k] = p;
        }
        if (chain)
        {
            p = pos;
            for (var k = 0; k < btCount; k++)
            {
                p = PrevIdx(p);
                if (p < 0 || !MatchOne(mode, btCd, btArr, k, buf.Info[p].Gid)) return false;
            }
            p = matched[^1];
            for (var k = 0; k < laCount; k++)
            {
                p = NextIdx(p);
                if (p < 0 || !MatchOne(mode, laCd, laArr, k, buf.Info[p].Gid)) return false;
            }
        }
        ApplyNested(records, substCount, matched);
        return true;
    }

    bool Format3(int sub, int pos, bool chain)
    {
        var o = sub + 2;
        int btCount = 0, btArr = 0, laCount = 0, laArr = 0;
        if (chain) { btCount = U16(o); btArr = o + 2; o += 2 + btCount * 2; }
        var inCount = U16(o); var inArr = o + 2;
        int substCount, records;
        if (chain)
        {
            o += 2 + inCount * 2;
            laCount = U16(o); laArr = o + 2; o += 2 + laCount * 2;
            substCount = U16(o); records = o + 2;
        }
        else
        {
            substCount = U16(o + 2); inArr = o + 4; records = inArr + inCount * 2;
        }
        if (inCount == 0) return false;
        if (Ot.Coverage(_d, sub + U16(inArr), buf.Info[pos].Gid) < 0) return false;
        var matched = new int[inCount];
        matched[0] = pos;
        var p = pos;
        for (var k = 1; k < inCount; k++)
        {
            p = NextIdx(p);
            if (p < 0 || (buf.Info[p].Mask & _mask) == 0 || Ot.Coverage(_d, sub + U16(inArr + k * 2), buf.Info[p].Gid) < 0) return false;
            matched[k] = p;
        }
        if (chain)
        {
            p = pos;
            for (var k = 0; k < btCount; k++)
            {
                p = PrevIdx(p);
                if (p < 0 || Ot.Coverage(_d, sub + U16(btArr + k * 2), buf.Info[p].Gid) < 0) return false;
            }
            p = matched[^1];
            for (var k = 0; k < laCount; k++)
            {
                p = NextIdx(p);
                if (p < 0 || Ot.Coverage(_d, sub + U16(laArr + k * 2), buf.Info[p].Gid) < 0) return false;
            }
        }
        ApplyNested(records, substCount, matched);
        return true;
    }

    void ApplyNested(int records, int count, int[] matched)
    {
        var lastIdx = matched[^1];
        var total = 0;
        if (_depth >= MaxDepth) { _next = lastIdx + 1; return; }
        var saveFlags = _flags; var saveSet = _markSet;
        for (var r = 0; r < count; r++)
        {
            var seq = U16(records + r * 4);
            var li = U16(records + r * 4 + 2);
            if (seq >= matched.Length) continue;
            var at = matched[seq];
            if (at < 0 || at >= buf.Len) continue;
            var lk = layout.LookupOffset(_gpos, li);
            if (lk < 0) continue;
            var before = buf.Len;
            _depth++;
            SetFlags(lk);
            _lengthDelta = 0;
            var saveNext = _next;
            ApplyAt(lk, at);
            _next = saveNext;
            _depth--;
            var delta = buf.Len - before;
            if (delta != 0)
            {
                total += delta;
                for (var j = seq + 1; j < matched.Length; j++) matched[j] = Math.Max(matched[j] + delta, at);
                if (matched.Length > 0) lastIdx = matched[^1];
            }
        }
        _flags = saveFlags; _markSet = saveSet;
        _next = Math.Min(Math.Max(lastIdx + 1, matched[0] + 1), buf.Len);
    }

    // ---------- GPOS ----------
    static int PopCount(int v) => System.Numerics.BitOperations.PopCount((uint)v);

    int ValueSize(int format) => PopCount(format & 0xFF) * 2;

    void ReadValue(int format, int o, out int xp, out int yp, out int xa, out int ya)
    {
        xp = yp = xa = ya = 0;
        if ((format & 1) != 0) { xp = S16(o); o += 2; }
        if ((format & 2) != 0) { yp = S16(o); o += 2; }
        if ((format & 4) != 0) { xa = S16(o); o += 2; }
        if ((format & 8) != 0) { ya = S16(o); }
    }

    void ApplyValue(int format, int o, int idx)
    {
        if ((format & 0xF) == 0) return;
        ReadValue(format, o, out var xp, out var yp, out var xa, out var ya);
        buf.Pos[idx].XOff += xp; buf.Pos[idx].YOff += yp; buf.Pos[idx].XAdv += xa; buf.Pos[idx].YAdv += ya;
    }

    bool ApplyGpos(int type, int sub, int pos)
    {
        var gid = buf.Info[pos].Gid;
        var fmt = U16(sub);
        switch (type)
        {
            case 1:
            {
                var ci = Ot.Coverage(_d, sub + U16(sub + 2), gid);
                if (ci < 0) return false;
                var vf = U16(sub + 4);
                if (fmt == 1) ApplyValue(vf, sub + 6, pos);
                else if (ci < U16(sub + 6)) ApplyValue(vf, sub + 8 + ci * ValueSize(vf), pos);
                else return false;
                return true;
            }
            case 2: return Pair(sub, pos, gid, fmt);
            case 3: return Cursive(sub, pos, gid);
            case 4: return MarkToBase(sub, pos, gid, ligature: false);
            case 5: return MarkToBase(sub, pos, gid, ligature: true);
            case 6: return MarkToMark(sub, pos, gid);
            case 7: return Context(sub, pos, chain: false);
            case 8: return Context(sub, pos, chain: true);
        }
        return false;
    }

    bool Pair(int sub, int pos, int gid, int fmt)
    {
        var ci = Ot.Coverage(_d, sub + U16(sub + 2), gid);
        if (ci < 0) return false;
        var second = NextIdx(pos);
        if (second < 0) return false;
        var g2 = buf.Info[second].Gid;
        var vf1 = U16(sub + 4); var vf2 = U16(sub + 6);
        var s1 = ValueSize(vf1); var s2 = ValueSize(vf2);
        int v1, v2;
        if (fmt == 1)
        {
            if (ci >= U16(sub + 8)) return false;
            var set = sub + U16(sub + 10 + ci * 2);
            var n = U16(set);
            var rec = 2 + s1 + s2;
            int lo = 0, hi = n - 1; v1 = v2 = -1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var r = set + 2 + mid * rec;
                var g = U16(r);
                if (g == g2) { v1 = r + 2; v2 = r + 2 + s1; break; }
                if (g < g2) lo = mid + 1; else hi = mid - 1;
            }
            if (v1 < 0) return false;
        }
        else
        {
            var cd1 = sub + U16(sub + 8); var cd2 = sub + U16(sub + 10);
            var c1n = U16(sub + 12); var c2n = U16(sub + 14);
            var c1 = Ot.Class(_d, cd1, gid); var c2 = Ot.Class(_d, cd2, g2);
            if (c1 >= c1n || c2 >= c2n) return false;
            var rec = s1 + s2;
            v1 = sub + 16 + (c1 * c2n + c2) * rec; v2 = v1 + s1;
        }
        ApplyValue(vf1, v1, pos);
        ApplyValue(vf2, v2, second);
        _next = vf2 != 0 ? second + 1 : second;
        return true;
    }

    void Anchor(int off, out int x, out int y)
    {
        x = S16(off + 2); y = S16(off + 4);
    }

    bool Cursive(int sub, int pos, int gid)
    {
        var ci = Ot.Coverage(_d, sub + U16(sub + 2), gid);
        if (ci < 0 || ci >= U16(sub + 4)) return false;
        var entry = U16(sub + 6 + ci * 4);
        if (entry == 0) return false;
        var prev = PrevIdx(pos);
        if (prev < 0) return false;
        var pi = Ot.Coverage(_d, sub + U16(sub + 2), buf.Info[prev].Gid);
        if (pi < 0 || pi >= U16(sub + 4)) return false;
        var exit = U16(sub + 6 + pi * 4 + 2);
        if (exit == 0) return false;
        Anchor(sub + entry, out var ex, out var ey);
        Anchor(sub + exit, out var xx, out var xy);
        int i = prev, j = pos;
        ref var pi_ = ref buf.Pos[i]; ref var pj = ref buf.Pos[j];
        if (!rtl)
        {
            pi_.XAdv = xx + pi_.XOff;
            var d = ex + pj.XOff;
            pj.XAdv -= d; pj.XOff -= d;
        }
        else
        {
            var d = xx + pi_.XOff;
            pi_.XAdv -= d; pi_.XOff -= d;
            pj.XAdv = ex + pj.XOff;
        }
        if ((_flags & 1) != 0)
        {
            pi_.AttachType = GlyphPos.AttachCursive; pi_.AttachChain = j - i; pi_.YOff = ey - xy;
        }
        else
        {
            pj.AttachType = GlyphPos.AttachCursive; pj.AttachChain = i - j; pj.YOff = xy - ey;
        }
        _next = pos + 1;
        return true;
    }

    int PrevNonMark(int pos)
    {
        for (var i = pos - 1; i >= 0; i--) if (buf.Info[i].Class != 3) return i;
        return -1;
    }

    bool MarkToBase(int sub, int pos, int gid, bool ligature)
    {
        var mi = Ot.Coverage(_d, sub + U16(sub + 2), gid);
        if (mi < 0) return false;
        var baseIdx = PrevNonMark(pos);
        if (baseIdx < 0) return false;
        var bi = Ot.Coverage(_d, sub + U16(sub + 4), buf.Info[baseIdx].Gid);
        if (bi < 0) return false;
        var classCount = U16(sub + 6);
        var markArray = sub + U16(sub + 8); var baseArray = sub + U16(sub + 10);
        if (mi >= U16(markArray)) return false;
        var markClass = U16(markArray + 2 + mi * 4);
        var markAnchor = U16(markArray + 2 + mi * 4 + 2);
        if (markAnchor == 0 || markClass >= classCount) return false;
        int baseAnchor;
        if (!ligature)
        {
            if (bi >= U16(baseArray)) return false;
            var a = U16(baseArray + 2 + (bi * classCount + markClass) * 2);
            if (a == 0) return false;
            baseAnchor = baseArray + a;
        }
        else
        {
            if (bi >= U16(baseArray)) return false;
            var attach = baseArray + U16(baseArray + 2 + bi * 2);
            var comps = U16(attach);
            if (comps == 0) return false;
            var bInfo = buf.Info[baseIdx]; var mInfo = buf.Info[pos];
            var comp = comps - 1;
            if (mInfo.LigId != 0 && mInfo.LigId == bInfo.LigId && mInfo.LigComp > 0) comp = Math.Min(comps, mInfo.LigComp) - 1;
            var a = U16(attach + 2 + (comp * classCount + markClass) * 2);
            if (a == 0) return false;
            baseAnchor = attach + a;
        }
        AttachMark(pos, baseIdx, baseAnchor, markArray + markAnchor);
        return true;
    }

    void AttachMark(int mark, int baseIdx, int baseAnchorOff, int markAnchorOff)
    {
        Anchor(baseAnchorOff, out var bx, out var by);
        Anchor(markAnchorOff, out var mx, out var my);
        ref var p = ref buf.Pos[mark];
        p.XOff = bx - mx; p.YOff = by - my;
        p.AttachType = GlyphPos.AttachMark; p.AttachChain = baseIdx - mark;
    }

    bool MarkToMark(int sub, int pos, int gid)
    {
        var mi = Ot.Coverage(_d, sub + U16(sub + 2), gid);
        if (mi < 0) return false;
        var prev = PrevIdx(pos);
        if (prev < 0 || buf.Info[prev].Class != 3) return false;
        var m2 = buf.Info[prev]; var m1 = buf.Info[pos];
        if (m1.LigId != m2.LigId || m1.LigComp != m2.LigComp) return false;
        var bi = Ot.Coverage(_d, sub + U16(sub + 4), m2.Gid);
        if (bi < 0) return false;
        var classCount = U16(sub + 6);
        var markArray = sub + U16(sub + 8); var baseArray = sub + U16(sub + 10);
        if (mi >= U16(markArray) || bi >= U16(baseArray)) return false;
        var markClass = U16(markArray + 2 + mi * 4);
        var markAnchor = U16(markArray + 2 + mi * 4 + 2);
        if (markAnchor == 0 || markClass >= classCount) return false;
        var a = U16(baseArray + 2 + (bi * classCount + markClass) * 2);
        if (a == 0) return false;
        AttachMark(pos, prev, baseArray + a, markArray + markAnchor);
        return true;
    }

    /// <summary>Resolves mark and cursive attachment chains into absolute offsets, then zeroes GDEF mark advances.</summary>
    public void FinishPositions()
    {
        var done = new bool[buf.Len];
        for (var i = 0; i < buf.Len; i++) Resolve(i, done, 0);
        for (var i = 0; i < buf.Len; i++)
            if (buf.Info[i].Class == 3) { buf.Pos[i].XAdv = 0; buf.Pos[i].YAdv = 0; }
    }

    void Resolve(int j, bool[] done, int depth)
    {
        if (done[j]) return;
        done[j] = true;
        ref var p = ref buf.Pos[j];
        if (p.AttachType == 0 || depth > 32) return;
        var i = j + p.AttachChain;
        if (i < 0 || i >= buf.Len || i == j) { p.AttachType = 0; return; }
        Resolve(i, done, depth + 1);
        if (p.AttachType == GlyphPos.AttachCursive)
        {
            p.YOff += buf.Pos[i].YOff;
            return;
        }
        p.XOff += buf.Pos[i].XOff; p.YOff += buf.Pos[i].YOff;
        if (!rtl)
        {
            for (var k = i; k < j; k++) { p.XOff -= buf.Pos[k].XAdv; p.YOff -= buf.Pos[k].YAdv; }
        }
        else
        {
            for (var k = i + 1; k <= j; k++) { p.XOff += buf.Pos[k].XAdv; p.YOff += buf.Pos[k].YAdv; }
        }
    }
}
