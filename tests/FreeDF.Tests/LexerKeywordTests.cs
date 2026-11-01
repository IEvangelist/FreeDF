using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class LexerKeywordTests
{
    [Theory]
    [InlineData("42 00000 R", 0)]
    [InlineData("42 65535 R", 65535)]
    [InlineData("42% comment\n99999% comment\nR", 99999)]
    public void Reference_generations_preserve_digits_comments_and_boundaries(string input, int generation)
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes(input + "/Next"));
        Assert.Equal(new PdfRef(42, generation), lexer.ReadObject());
        Assert.Equal(input.Length, lexer.Pos);
        Assert.Equal(new PdfName("Next"), lexer.ReadObject());
    }

    [Theory]
    [InlineData("42 000000 R")]
    [InlineData("42 3 RG")]
    [InlineData("42 /Name")]
    public void Failed_reference_lookahead_restores_the_position(string input)
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes(input));
        Assert.Equal(new PdfInteger(42), lexer.ReadObject());
        Assert.Equal(2, lexer.Pos);
        Assert.NotNull(lexer.ReadObject());
    }

    const string Operators =
        "q Q cm w J j M d ri i gs m l c v y h re S s f F f* B B* b b* n W W* " +
        "BT ET Tc Tw Tz TL Tf Tr Ts Td TD Tm T* Tj TJ ' \" d0 d1 " +
        "CS cs SC SCN sc scn G g RG rg K k sh Do BI ID EI MP DP BMC BDC EMC BX EX";

    public static IEnumerable<object[]> Keywords() =>
        Operators.Split(' ').Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Keywords))]
    public void Operators_preserve_values_boundaries_and_record_copy_semantics(string token)
    {
        foreach (var resolveRefs in new[] { false, true })
        {
            var bytes = Encoding.Latin1.GetBytes(token + "% comment\r\n/" + token);
            var lexer = new PdfLexer(bytes);
            var keyword = Assert.IsType<PdfKeyword>(lexer.ReadObject(resolveRefs));
            Assert.Equal(token, keyword.Value);
            Assert.Equal(token.Length, lexer.Pos);
            Assert.Equal(token, Assert.IsType<PdfName>(lexer.ReadObject(resolveRefs)).Value);
            Assert.Null(lexer.ReadObject(resolveRefs));
            bytes.AsSpan().Fill((byte)'x');
            Assert.Equal(token, keyword.Value);
            Assert.Equal("changed", (keyword with { Value = "changed" }).Value);
            Assert.Equal(token, Assert.IsType<PdfKeyword>(
                new PdfLexer(Encoding.Latin1.GetBytes(token)).ReadObject(resolveRefs)).Value);
        }
    }

    [Fact]
    public void Short_keyword_lookup_does_not_match_prefixes_suffixes_or_non_ascii_bytes()
    {
        foreach (var token in Operators.Split(' '))
            foreach (var unknown in new[] { token + "x", "x" + token, token + "\u00ff", "\u0080" + token })
            {
                var lexer = new PdfLexer(Encoding.Latin1.GetBytes(unknown));
                Assert.Equal(unknown, Assert.IsType<PdfKeyword>(lexer.ReadObject(false)).Value);
                Assert.Equal(unknown.Length, lexer.Pos);
            }
    }

    [Fact]
    public void Repeated_operators_do_not_allocate_token_strings_or_records()
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes(Operators));
        var expectedCount = Operators.Split(' ').Length;
        for (var i = 0; i < 100; i++)
        {
            lexer.Pos = 0;
            while (lexer.ReadObject(false) is not null) { }
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        for (var i = 0; i < 100; i++)
        {
            lexer.Pos = 0;
            while (lexer.ReadObject(false) is PdfKeyword) count++;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(100 * expectedCount, count);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Numbers_references_booleans_and_null_keep_their_types_and_positions()
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes("q 123 0 R 5 0 RG --5 .junk 5- true false null"));
        Assert.Equal("q", Assert.IsType<PdfKeyword>(lexer.ReadObject()).Value);
        Assert.Equal(new PdfRef(123, 0), lexer.ReadObject());
        Assert.Equal(9, lexer.Pos);
        Assert.Equal(new PdfInteger(5), lexer.ReadObject());
        Assert.Equal(11, lexer.Pos);
        Assert.Equal(new PdfInteger(0), lexer.ReadObject());
        Assert.Equal("RG", Assert.IsType<PdfKeyword>(lexer.ReadObject()).Value);
        Assert.Equal(new PdfInteger(-5), lexer.ReadObject());
        Assert.Equal(new PdfReal(0), lexer.ReadObject());
        Assert.Equal("5-", Assert.IsType<PdfKeyword>(lexer.ReadObject()).Value);
        Assert.Equal(new PdfBoolean(true), lexer.ReadObject());
        Assert.Equal(new PdfBoolean(false), lexer.ReadObject());
        Assert.Same(PdfNull.Instance, lexer.ReadObject());
        Assert.Null(lexer.ReadObject());

        lexer = new PdfLexer("123 0 R"u8.ToArray());
        Assert.Equal(new PdfInteger(123), lexer.ReadObject(false));
        Assert.Equal(new PdfInteger(0), lexer.ReadObject(false));
        Assert.Equal("R", Assert.IsType<PdfKeyword>(lexer.ReadObject(false)).Value);
    }

    [Fact]
    public void Names_strings_and_containers_remain_independent_of_keywords_and_source_bytes()
    {
        var bytes = "[q /q (q) <71> << /q Q >> q]"u8.ToArray();
        var lexer = new PdfLexer(bytes);
        var array = Assert.IsType<PdfArray>(lexer.ReadObject());
        Assert.Equal(6, array.Count);
        Assert.Equal(new PdfKeyword("q"), array[0]);
        Assert.Equal(new PdfName("q"), array[1]);
        var literal = Assert.IsType<PdfString>(array[2]);
        var hex = Assert.IsType<PdfString>(array[3]);
        Assert.False(literal.IsHex);
        Assert.True(hex.IsHex);
        Assert.Equal(new PdfKeyword("Q"), Assert.IsType<PdfDictionary>(array[4]).Get("q"));
        Assert.Equal(new PdfKeyword("q"), array[5]);
        Assert.Null(lexer.ReadObject());
        bytes.AsSpan().Clear();
        Assert.Equal("q"u8.ToArray(), literal.Bytes);
        Assert.Equal("q"u8.ToArray(), hex.Bytes);
        literal.Bytes[0] = (byte)'x';
        Assert.Equal("q", hex.Text);
        Assert.Equal(new PdfKeyword("q"), array[0]);
    }

    [Theory]
    [InlineData("[q", 2)]
    [InlineData("<< /q", 5)]
    [InlineData("<< /q >>", 8)]
    public void Truncated_containers_keep_tolerant_behavior(string input, int end)
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes(input));
        var value = lexer.ReadObject();
        if (input[0] == '[')
            Assert.Equal(new PdfKeyword("q"), Assert.Single(Assert.IsType<PdfArray>(value).Items));
        else if (input.EndsWith(">>", StringComparison.Ordinal))
            Assert.Same(PdfNull.Instance, Assert.IsType<PdfDictionary>(value).Get("q"));
        else
            Assert.Empty(Assert.IsType<PdfDictionary>(value).Entries);
        Assert.Equal(end, lexer.Pos);
        Assert.Null(lexer.ReadObject());
    }

    [Theory]
    [InlineData("[", "]")]
    [InlineData("<< /q ", ">>")]
    public void Excessive_nesting_keeps_the_same_diagnostic(string open, string close)
    {
        var input = string.Concat(Enumerable.Repeat(open, 201)) + "q" +
            string.Concat(Enumerable.Repeat(close, 201));
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes(input));
        var error = Assert.Throws<PdfParseException>(() => lexer.ReadObject());
        Assert.Equal("object nesting too deep", error.Message);
        Assert.Equal(open.Length * 200 + (open[0] == '[' ? 1 : 2), lexer.Pos);
    }
}
