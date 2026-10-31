using System.Text.Json;
using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;

namespace AdoBye.Tests;

/// <summary>Editor-level behaviour for password-protected PDFs: password prompts, permission enforcement, protected save and protection removal.</summary>
public class EditorProtectionTests
{
    static readonly Dictionary<string, string> None = [];
    static byte[] Sample(string name) => File.ReadAllBytes(Path.Combine(SamplesTests.SamplesDir()!, name));
    static Dictionary<string, string> Q(params (string, string)[] q) => q.ToDictionary(x => x.Item1, x => x.Item2);
    static JsonElement Json(EditorSession.Reply r) => JsonDocument.Parse(r.Data).RootElement.Clone();
    static EditorSession Open(string file, string? password = null)
    {
        var s = new EditorSession(new FontCatalog());
        s.Handle("POST", "/api/open", password is null ? None : Q(("password", password)), Sample(file), file);
        return s;
    }

    [Fact]
    public void Password_prompt_codes_distinguish_missing_from_wrong()
    {
        var s = new EditorSession(new FontCatalog());
        var missing = Assert.Throws<ArgumentException>(() => s.Handle("POST", "/api/open", None, Sample("17-encrypted-user-password.pdf"), "a.pdf"));
        Assert.StartsWith("password-required:", missing.Message);
        var wrong = Assert.Throws<ArgumentException>(() => s.Handle("POST", "/api/open", Q(("password", "nope")), Sample("17-encrypted-user-password.pdf"), "a.pdf"));
        Assert.StartsWith("password-incorrect:", wrong.Message);
        Assert.DoesNotContain("nope", wrong.Message);
        Assert.Equal(0, Json(s.Handle("GET", "/api/state", None)).GetProperty("pageCount").GetInt32());
    }

    [Fact]
    public void User_password_opens_with_restrictions_and_no_recovery()
    {
        var s = Open("17-encrypted-user-password.pdf", "user123");
        var state = Json(s.Handle("GET", "/api/state", None));
        Assert.True(state.GetProperty("pageCount").GetInt32() > 0);
        Assert.False(state.GetProperty("recoverySupported").GetBoolean());
        Assert.Equal("Aes256", state.GetProperty("protection").GetProperty("method").GetString());
        Assert.False(state.GetProperty("protection").GetProperty("owner").GetBoolean());
    }

    [Fact]
    public void Restricted_sample_enforces_permissions_with_clear_messages()
    {
        var s = Open("18-encrypted-owner-restricted.pdf");
        var p = Json(s.Handle("GET", "/api/state", None)).GetProperty("protection");
        Assert.False(p.GetProperty("copy").GetBoolean());
        Assert.False(p.GetProperty("modify").GetBoolean());
        var text = Assert.Throws<NotSupportedException>(() => s.Handle("GET", "/api/export/text", None));
        Assert.Contains("owner password", text.Message);
        var rotate = Assert.Throws<NotSupportedException>(() => s.Handle("POST", "/api/rotate/0/90", None));
        Assert.Contains("owner password", rotate.Message);
        Assert.Throws<NotSupportedException>(() => s.Handle("GET", "/api/export/pdf", None));
        Assert.Throws<ArgumentException>(() => s.Handle("GET", "/api/export/secure", None));
        var owner = Assert.Throws<NotSupportedException>(() => s.Handle("GET", "/api/export/secure", Q(("remove", "1"))));
        Assert.Contains("owner", owner.Message);
    }

    [Fact]
    public void Owner_can_edit_reprotect_and_remove_protection()
    {
        var s = Open("18-encrypted-owner-restricted.pdf", "owner123");
        Assert.True(Json(s.Handle("GET", "/api/state", None)).GetProperty("protection").GetProperty("owner").GetBoolean());
        Assert.NotEmpty(s.Handle("GET", "/api/export/text", None).Data);

        var plain = s.Handle("GET", "/api/export/secure", Q(("remove", "1"))).Data;
        Assert.False(PdfDocument.Load(plain).File.IsEncrypted);

        var protectedCopy = s.Handle("GET", "/api/export/secure", Q(("user", "u-1"), ("owner", "o-1"), ("perms", "print,access"))).Data;
        Assert.Throws<PdfPasswordException>(() => PdfDocument.Load(protectedCopy));
        var opened = PdfDocument.Load(protectedCopy, "u-1");
        Assert.Equal(PdfPermissions.Print | PdfPermissions.ExtractAccessibility, opened.File.Decryptor!.PermissionFlags);
        Assert.Equal(PdfEncryptionMethod.Aes256, opened.File.Decryptor.EncryptionMethod);
        Assert.True(PdfDocument.Load(protectedCopy, "o-1").File.Decryptor!.IsOwner);

        Assert.Throws<ArgumentException>(() => s.Handle("GET", "/api/export/secure", Q(("user", "u"), ("perms", "print"))));
        Assert.Throws<ArgumentException>(() => s.Handle("GET", "/api/export/secure", Q(("user", "u"), ("perms", "bogus"))));
    }

    [Fact]
    public void Non_owner_cannot_widen_permissions_on_a_protected_copy()
    {
        var s = Open("18-encrypted-owner-restricted.pdf");
        var copy = s.Handle("GET", "/api/export/secure", Q(("user", "u"), ("owner", "new-owner"), ("perms", "print,copy,modify,annotate,fill,assemble"))).Data;
        var flags = PdfDocument.Load(copy, "u").File.Decryptor!.PermissionFlags;
        Assert.False(flags.HasFlag(PdfPermissions.Copy));
        Assert.False(flags.HasFlag(PdfPermissions.Modify));
    }

    [Fact]
    public void Unprotected_documents_can_be_protected_with_aes256()
    {
        var s = new EditorSession(new FontCatalog());
        s.Handle("POST", "/api/new", None);
        var bytes = s.Handle("GET", "/api/export/secure", Q(("user", "pw"), ("owner", "ow"))).Data;
        var d = PdfDocument.Load(bytes, "pw").File.Decryptor!;
        Assert.Equal(PdfEncryptionMethod.Aes256, d.EncryptionMethod);
        Assert.False(d.IsOwner);
    }
}
