using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

public class OptionalContentMembershipPolicyTests
{
    public static IEnumerable<object[]> Policies()
    {
        foreach (var policy in new[] { "", "/P /AnyOn", "/P /AllOn", "/P /AnyOff", "/P /AllOff" })
            foreach (var members in new[] { "", "/OCGs []", "/OCGs [null 99 0 R]", "/OCGs 5 0 R",
                "/OCGs [5 0 R]", "/OCGs [6 0 R]", "/OCGs [null 5 0 R 99 0 R 6 0 R]" })
            {
                var anyOn = members.Contains("5 0 R");
                var anyOff = members.Contains("6 0 R");
                var visible = !anyOn && !anyOff || policy switch
                {
                    "/P /AllOn" => !anyOff,
                    "/P /AnyOff" => anyOff,
                    "/P /AllOff" => !anyOn,
                    _ => anyOn
                };
                yield return [policy, members, visible];
            }
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void Membership_policies_preserve_all_member_states_and_ignore_null_references(
        string policy, string members, bool visible) => AssertRendering(policy, members, visible);

    [Theory]
    [InlineData("/P /AnyOn", true)]
    [InlineData("/P /AllOn", false)]
    [InlineData("/P /AnyOff", true)]
    [InlineData("/P /AllOff", false)]
    public void Every_member_is_evaluated_even_after_the_policy_result_is_known(string policy, bool visible) =>
        AssertRendering(policy, "/OCGs [5 0 R 6 0 R]", visible, diagnosePeer: true);

    static void AssertRendering(string policy, string members, bool visible, bool diagnosePeer = false)
    {
        static PdfObject Parse(string text) => new PdfLexer(Encoding.ASCII.GetBytes(text)).ReadObject()!;
        var objects = new Dictionary<int, PdfObject>
        {
            [1] = Parse("<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [5 0 R 6 0 R] " +
                "/D << /BaseState /ON /OFF [6 0 R] " +
                (diagnosePeer ? "/AS [<< /Event /View /Category [/Unknown] /OCGs [6 0 R] >>] " : "") + ">> >> >>"),
            [2] = Parse("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = Parse("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R " +
                "/Resources << /Properties << /Layer 7 0 R >> >> >>"),
            [4] = new PdfStream(new PdfDictionary(),
                Encoding.ASCII.GetBytes("/OC /Layer BDC 1 0 0 rg 0 0 200 200 re f EMC")),
            [5] = Parse("<< /Type /OCG /Name (On) >>"),
            [6] = Parse("<< /Type /OCG /Name (Off) " + (diagnosePeer ? "/Usage << >> " : "") + ">>"),
            [7] = Parse($"<< /Type /OCMD {members} {policy} >>")
        };
        var document = PdfDocument.Load(PdfWriter.Write(objects, 1, null,
            options: new PdfWriteOptions { FileId = new byte[16] }));
        Assert.False(document.File.Repaired);
        foreach (var state in new OptionalContentState?[] { null, new(document) })
        {
            var rendered = DocumentRenderer.RenderPage(document.Pages[0],
                new PdfRenderOptions { OptionalContentState = state }, 72);
            var pixel = (50 * rendered.Width + 50) * 4;
            Assert.Equal(visible ? new byte[] { 255, 0, 0, 255 } : [255, 255, 255, 255],
                rendered.Rgba[pixel..(pixel + 4)]);
            if (diagnosePeer)
                Assert.Contains("unsupported optional-content usage category Unknown", rendered.Warnings);
            else
                Assert.Empty(rendered.Warnings);
        }
    }
}
