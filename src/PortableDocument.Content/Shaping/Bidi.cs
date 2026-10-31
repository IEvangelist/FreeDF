namespace PortableDocument.Content.Shaping;

public enum BidiDirection { Auto, LeftToRight, RightToLeft }

/// <summary>A maximal range of characters sharing one resolved embedding level, in logical (text) indices.</summary>
public readonly record struct BidiRun(int Start, int Length, int Level)
{
    public bool RightToLeft => (Level & 1) != 0;
}

/// <summary>Result of the Unicode Bidirectional Algorithm (UAX #9) for one paragraph/line.</summary>
public sealed class BidiResult
{
    public int ParagraphLevel { get; }
    /// <summary>Resolved embedding level for every UTF-16 unit of the text (rule L1 applied, X9-removed characters take the preceding level).</summary>
    public byte[] Levels { get; }
    internal BidiResult(int level, byte[] levels) { ParagraphLevel = level; Levels = levels; }

    public bool IsMixed
    {
        get { foreach (var l in Levels) if (l != Levels[0]) return true; return false; }
    }

    /// <summary>Level runs in visual (left-to-right) order (rule L2 at run granularity). Characters inside an odd-level run are displayed right to left.</summary>
    public List<BidiRun> VisualRuns()
    {
        var runs = new List<BidiRun>();
        var n = Levels.Length;
        for (var i = 0; i < n;)
        {
            var j = i + 1;
            while (j < n && Levels[j] == Levels[i]) j++;
            runs.Add(new BidiRun(i, j - i, Levels[i]));
            i = j;
        }
        if (runs.Count == 0) return runs;
        var max = 0; var minOdd = 255;
        foreach (var r in runs) { max = Math.Max(max, r.Level); if ((r.Level & 1) != 0) minOdd = Math.Min(minOdd, r.Level); }
        for (var lvl = max; lvl >= minOdd && lvl > 0; lvl--)
        {
            for (var i = 0; i < runs.Count;)
            {
                if (runs[i].Level < lvl) { i++; continue; }
                var j = i;
                while (j < runs.Count && runs[j].Level >= lvl) j++;
                runs.Reverse(i, j - i);
                i = j;
            }
        }
        return runs;
    }
}

/// <summary>Unicode Bidirectional Algorithm: explicit embeddings/overrides/isolates (X1-X10), weak (W1-W7), neutral and bracket-pair (N0-N2), implicit (I1-I2) and L1 rules.</summary>
public static class Bidi
{
    const int MaxDepth = 125;

    public static BidiResult Resolve(string text, BidiDirection direction = BidiDirection.Auto)
    {
        var cps = new List<int>(text.Length); var starts = new List<int>(text.Length);
        for (var i = 0; i < text.Length;)
        {
            var cp = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
            cps.Add(cp); starts.Add(i);
            i += cp > 0xFFFF ? 2 : 1;
        }
        var n = cps.Count;
        var orig = new BidiClass[n];
        for (var i = 0; i < n; i++) orig[i] = UnicodeData.GetBidiClass(cps[i]);

        var matchPdi = new int[n]; var matchInit = new int[n];
        Array.Fill(matchPdi, -1); Array.Fill(matchInit, -1);
        var stack = new Stack<int>();
        for (var i = 0; i < n; i++)
        {
            if (orig[i] is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI) stack.Push(i);
            else if (orig[i] == BidiClass.PDI && stack.Count > 0) { var o = stack.Pop(); matchPdi[o] = i; matchInit[i] = o; }
        }

        int FirstStrong(int from, int to, bool skipIsolates)
        {
            for (var i = from; i < to; i++)
            {
                var c = orig[i];
                if (c == BidiClass.L) return 0;
                if (c is BidiClass.R or BidiClass.AL) return 1;
                if (skipIsolates && c is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI)
                {
                    if (matchPdi[i] < 0) return -1;
                    i = matchPdi[i];
                }
            }
            return -1;
        }

        var paraLevel = direction switch { BidiDirection.LeftToRight => 0, BidiDirection.RightToLeft => 1, _ => Math.Max(FirstStrong(0, n, true), 0) };

        var levels = new byte[n];
        var cls = (BidiClass[])orig.Clone();
        var stk = new (int Level, BidiClass Override, bool Isolate)[MaxDepth + 2];
        var sp = 0;
        stk[sp++] = (paraLevel, BidiClass.ON, false);
        int overflowIsolate = 0, overflowEmbedding = 0, validIsolate = 0;
        var removed = new bool[n];
        for (var i = 0; i < n; i++)
        {
            var c = orig[i];
            var top = stk[sp - 1];
            switch (c)
            {
                case BidiClass.RLE or BidiClass.LRE or BidiClass.RLO or BidiClass.LRO:
                {
                    levels[i] = (byte)top.Level; removed[i] = true;
                    var rtl = c is BidiClass.RLE or BidiClass.RLO;
                    var nl = rtl ? (top.Level + 1) | 1 : (top.Level + 2) & ~1;
                    if (nl <= MaxDepth && overflowIsolate == 0 && overflowEmbedding == 0)
                        stk[sp++] = (nl, c == BidiClass.RLO ? BidiClass.R : c == BidiClass.LRO ? BidiClass.L : BidiClass.ON, false);
                    else if (overflowIsolate == 0) overflowEmbedding++;
                    break;
                }
                case BidiClass.RLI or BidiClass.LRI or BidiClass.FSI:
                {
                    levels[i] = (byte)top.Level;
                    if (top.Override != BidiClass.ON) cls[i] = top.Override;
                    var rtl = c == BidiClass.RLI;
                    if (c == BidiClass.FSI) rtl = FirstStrong(i + 1, matchPdi[i] < 0 ? n : matchPdi[i], true) == 1;
                    var nl = rtl ? (top.Level + 1) | 1 : (top.Level + 2) & ~1;
                    if (nl <= MaxDepth && overflowIsolate == 0 && overflowEmbedding == 0) { validIsolate++; stk[sp++] = (nl, BidiClass.ON, true); }
                    else overflowIsolate++;
                    break;
                }
                case BidiClass.PDI:
                {
                    if (overflowIsolate > 0) overflowIsolate--;
                    else if (validIsolate > 0)
                    {
                        overflowEmbedding = 0;
                        while (!stk[sp - 1].Isolate) sp--;
                        sp--; validIsolate--;
                    }
                    top = stk[sp - 1];
                    levels[i] = (byte)top.Level;
                    if (top.Override != BidiClass.ON) cls[i] = top.Override;
                    break;
                }
                case BidiClass.PDF:
                {
                    levels[i] = (byte)top.Level; removed[i] = true;
                    if (overflowIsolate > 0) { }
                    else if (overflowEmbedding > 0) overflowEmbedding--;
                    else if (!top.Isolate && sp >= 2) sp--;
                    break;
                }
                case BidiClass.B: levels[i] = (byte)paraLevel; break;
                case BidiClass.BN: levels[i] = (byte)top.Level; removed[i] = true; break;
                default:
                    levels[i] = (byte)top.Level;
                    if (top.Override != BidiClass.ON) cls[i] = top.Override;
                    break;
            }
        }

        // X10: level runs over the non-removed characters, chained across matching isolate initiator / PDI pairs.
        var kept = new List<int>(n);
        for (var i = 0; i < n; i++) if (!removed[i]) kept.Add(i);
        var runs = new List<List<int>>();
        var runOf = new int[n];
        for (var k = 0; k < kept.Count;)
        {
            var run = new List<int>();
            var lvl = levels[kept[k]];
            while (k < kept.Count && levels[kept[k]] == lvl) { run.Add(kept[k]); runOf[kept[k]] = runs.Count; k++; }
            runs.Add(run);
        }
        var usedRun = new bool[runs.Count];
        var sequences = new List<List<int>>();
        for (var r = 0; r < runs.Count; r++)
        {
            if (usedRun[r]) continue;
            var first = runs[r][0];
            if (orig[first] == BidiClass.PDI && matchInit[first] >= 0) continue;
            var seq = new List<int>();
            var cur = r;
            while (true)
            {
                usedRun[cur] = true;
                seq.AddRange(runs[cur]);
                var last = runs[cur][^1];
                if (orig[last] is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI && matchPdi[last] >= 0 && !removed[matchPdi[last]]) cur = runOf[matchPdi[last]];
                else break;
            }
            sequences.Add(seq);
        }
        for (var r = 0; r < runs.Count; r++)
            if (!usedRun[r]) { var seq = new List<int>(runs[r]); usedRun[r] = true; sequences.Add(seq); }

        var xl = (byte[])levels.Clone();
        foreach (var seq in sequences) ResolveSequence(seq, cps, orig, cls, xl, levels, removed, paraLevel, matchPdi);

        // L1
        var inTrailing = true;
        for (var i = n - 1; i >= 0; i--)
        {
            var c = orig[i];
            if (c is BidiClass.B or BidiClass.S) { levels[i] = (byte)paraLevel; inTrailing = true; }
            else if (c is BidiClass.WS or BidiClass.FSI or BidiClass.LRI or BidiClass.RLI or BidiClass.PDI || removed[i]) { if (inTrailing) levels[i] = (byte)paraLevel; }
            else inTrailing = false;
        }
        // X9 removed characters take the level of the previous character.
        for (var i = 0; i < n; i++)
            if (removed[i]) levels[i] = i == 0 ? (byte)paraLevel : levels[i - 1];
        var outLevels = new byte[text.Length];
        for (var i = 0; i < n; i++)
        {
            outLevels[starts[i]] = levels[i];
            if (cps[i] > 0xFFFF) outLevels[starts[i] + 1] = levels[i];
        }
        return new BidiResult(paraLevel, outLevels);
    }

    /// <summary>Rule L2 over a sequence of items with resolved levels: the logical indices in visual left-to-right order.</summary>
    public static int[] VisualOrder(IReadOnlyList<int> levels)
    {
        var order = new int[levels.Count];
        for (var i = 0; i < order.Length; i++) order[i] = i;
        int max = 0, minOdd = int.MaxValue;
        foreach (var l in levels) { max = Math.Max(max, l); if ((l & 1) != 0) minOdd = Math.Min(minOdd, l); }
        for (var lvl = max; lvl >= minOdd && lvl > 0; lvl--)
            for (var i = 0; i < order.Length;)
            {
                if (levels[order[i]] < lvl) { i++; continue; }
                var j = i;
                while (j < order.Length && levels[order[j]] >= lvl) j++;
                Array.Reverse(order, i, j - i);
                i = j;
            }
        return order;
    }

    static BidiClass Dir(int level) => (level & 1) == 0 ? BidiClass.L : BidiClass.R;

    static void ResolveSequence(List<int> seq, List<int> cps, BidiClass[] orig, BidiClass[] cls, byte[] xl, byte[] levels, bool[] removed, int paraLevel, int[] matchPdi)
    {
        var count = seq.Count;
        var level = xl[seq[0]];
        var firstIdx = seq[0]; var lastIdx = seq[^1];
        // sos / eos
        var before = paraLevel;
        for (var i = firstIdx - 1; i >= 0; i--) if (!removed[i]) { before = xl[i]; break; }
        var after = paraLevel;
        var openIsolate = orig[lastIdx] is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI;
        if (!openIsolate) for (var i = lastIdx + 1; i < xl.Length; i++) if (!removed[i]) { after = xl[i]; break; }
        var sos = Dir(Math.Max(level, before)); var eos = Dir(Math.Max(level, after));

        var t = new BidiClass[count];
        for (var i = 0; i < count; i++) t[i] = cls[seq[i]];

        // W1
        for (var i = 0; i < count; i++)
            if (t[i] == BidiClass.NSM)
                t[i] = i == 0 ? sos : t[i - 1] is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI ? BidiClass.ON : t[i - 1];
        // W2, W3
        var strong = sos;
        for (var i = 0; i < count; i++)
        {
            if (t[i] is BidiClass.L or BidiClass.R or BidiClass.AL) strong = t[i];
            else if (t[i] == BidiClass.EN && strong == BidiClass.AL) t[i] = BidiClass.AN;
        }
        for (var i = 0; i < count; i++) if (t[i] == BidiClass.AL) t[i] = BidiClass.R;
        // W4
        for (var i = 1; i < count - 1; i++)
        {
            if (t[i] == BidiClass.ES && t[i - 1] == BidiClass.EN && t[i + 1] == BidiClass.EN) t[i] = BidiClass.EN;
            else if (t[i] == BidiClass.CS && t[i - 1] == t[i + 1] && t[i - 1] is BidiClass.EN or BidiClass.AN) t[i] = t[i - 1];
        }
        // W5
        for (var i = 0; i < count; i++)
        {
            if (t[i] != BidiClass.ET) continue;
            var j = i;
            while (j < count && t[j] == BidiClass.ET) j++;
            var adj = i > 0 && t[i - 1] == BidiClass.EN || j < count && t[j] == BidiClass.EN;
            if (adj) for (var k = i; k < j; k++) t[k] = BidiClass.EN;
            i = j - 1;
        }
        // W6
        for (var i = 0; i < count; i++) if (t[i] is BidiClass.ES or BidiClass.ET or BidiClass.CS) t[i] = BidiClass.ON;
        // W7
        strong = sos;
        for (var i = 0; i < count; i++)
        {
            if (t[i] is BidiClass.L or BidiClass.R) strong = t[i];
            else if (t[i] == BidiClass.EN && strong == BidiClass.L) t[i] = BidiClass.L;
        }

        // N0: paired brackets
        var e = Dir(level);
        var pairs = new List<(int Open, int Close)>();
        var bst = new List<(int Pair, int Pos)>();
        for (var i = 0; i < count; i++)
        {
            if (t[i] != BidiClass.ON || !UnicodeData.IsBracket(cps[seq[i]], out var pair, out var open)) continue;
            if (open)
            {
                if (bst.Count == 63) break;
                bst.Add((pair, i));
            }
            else
            {
                var c = cps[seq[i]];
                var canon = c == 0x232A ? 0x3009 : c;
                for (var k = bst.Count - 1; k >= 0; k--)
                    if (bst[k].Pair == canon) { pairs.Add((bst[k].Pos, i)); bst.RemoveRange(k, bst.Count - k); break; }
            }
        }
        pairs.Sort((a, b) => a.Open.CompareTo(b.Open));
        static BidiClass StrongOf(BidiClass c) => c switch { BidiClass.L => BidiClass.L, BidiClass.R or BidiClass.EN or BidiClass.AN => BidiClass.R, _ => BidiClass.ON };
        foreach (var (open, close) in pairs)
        {
            var foundE = false; var foundOpp = false;
            for (var i = open + 1; i < close; i++)
            {
                var s = StrongOf(t[i]);
                if (s == BidiClass.ON) continue;
                if (s == e) foundE = true; else foundOpp = true;
            }
            BidiClass? set = null;
            if (foundE) set = e;
            else if (foundOpp)
            {
                var ctx = sos;
                for (var i = open - 1; i >= 0; i--) { var s = StrongOf(t[i]); if (s != BidiClass.ON) { ctx = s; break; } }
                set = ctx != e ? ctx : e;
            }
            if (set is { } v)
            {
                t[open] = v; t[close] = v;
                for (var i = open + 1; i < count && orig[seq[i]] == BidiClass.NSM; i++) t[i] = v;
                for (var i = close + 1; i < count && orig[seq[i]] == BidiClass.NSM; i++) t[i] = v;
            }
        }
        // N1, N2
        static bool NI(BidiClass c) => c is BidiClass.B or BidiClass.S or BidiClass.WS or BidiClass.ON or BidiClass.FSI or BidiClass.LRI or BidiClass.RLI or BidiClass.PDI;
        for (var i = 0; i < count; i++)
        {
            if (!NI(t[i])) continue;
            var j = i;
            while (j < count && NI(t[j])) j++;
            var lead = i == 0 ? sos : StrongOf(t[i - 1]);
            var trail = j == count ? eos : StrongOf(t[j]);
            var res = lead == trail && lead != BidiClass.ON ? lead : e;
            for (var k = i; k < j; k++) t[k] = res;
            i = j - 1;
        }
        // I1, I2
        for (var i = 0; i < count; i++)
        {
            var l = (int)level;
            if ((l & 1) == 0) { if (t[i] == BidiClass.R) l += 1; else if (t[i] is BidiClass.AN or BidiClass.EN) l += 2; }
            else if (t[i] is BidiClass.L or BidiClass.EN or BidiClass.AN) l += 1;
            levels[seq[i]] = (byte)l;
        }
    }
}
