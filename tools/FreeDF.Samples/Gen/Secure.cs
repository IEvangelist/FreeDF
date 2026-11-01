using FreeDF.Samples.Kit;
using PortableDocument.Core;
using static FreeDF.Samples.Kit.Helpers;
using static FreeDF.Samples.Kit.Theme;

namespace FreeDF.Samples.Gen;

/// <summary>Shared page for the two encrypted samples; encryption is applied by this repo's PdfWriter (AES-256, V5/R6).</summary>
static class Secure
{
    public static Doc Page(string title, string kicker, string badge, (string Head, string Body)[] rows)
    {
        var d = new Doc(title, subject: kicker, lang: "en-US");
        var t = new Faces(d);
        var pg = d.Page(A4.W, A4.H);
        Background(pg, P["paper"]);
        Box(pg, 0, pg.H - 250, pg.W, 250, fill: P["ink"]);
        pg.Save(); pg.Rect(0, pg.H - 250, pg.W, 250); pg.Clip();
        pg.Gs(("ca", 0.5));
        pg.Sh(Radial(pg.W - 40, pg.H - 20, 0, pg.W - 40, pg.H - 20, 420, [(0, P["indigo"]), (1, P["ink"])]));
        pg.Restore();
        pg.Text(t.SansB, 9, M, pg.H - 60, kicker.ToUpperInvariant(), color: "#A5B4FC", tracking: 1.8, kern: false);
        pg.Text(t.SerifB, 40, M, pg.H - 130, title, color: "#FFFFFF");
        var w = t.SansB.Width(badge, 9, tracking: 1.4) + 24;
        Box(pg, M, pg.H - 200, w, 24, fill: P["rose"], r: 12);
        pg.Text(t.SansB, 9, M + 12, pg.H - 192.5, badge, color: "#FFFFFF", tracking: 1.4, kern: false);
        var y = pg.H - 310;
        foreach (var (head, body) in rows)
        {
            pg.Text(t.SansB, 8, M, y, head.ToUpperInvariant(), color: P["indigo"], tracking: 1.6, kern: false);
            y = Paragraph(pg, t.Serif, 12.5, M, y - 20, pg.W - 2 * M - 40, body, color: P["ink"], leading: 19) - 22;
        }
        return d;
    }

    public static PdfEncryption Encryption(string user, string owner, PdfPermissions allowed, string seed) => new()
    {
        UserPassword = user,
        OwnerPassword = owner,
        Permissions = PdfPermissionHelper.ToP(allowed),
        Method = PdfEncryptionMethod.Aes256,
        RandomBytes = new Drbg(seed).Bytes,
    };
}
