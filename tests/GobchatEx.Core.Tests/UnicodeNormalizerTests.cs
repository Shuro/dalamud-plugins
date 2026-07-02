using System.Text;
using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

/// <summary>
/// NFKC normalization used for chat matching only. WHY this matters: a rule written in plain ASCII
/// must still fire on a message typed in decorative code points (e.g. Mathematical Sans-Serif Bold
/// "𝗙𝗟𝗨𝗫"), yet the displayed text stays original — so the index map must translate a match found on
/// the folded copy back to the exact original substring, and the all-ASCII hot path must stay free.
/// </summary>
public sealed class UnicodeNormalizerTests
{
    // Map ASCII letters to their Mathematical Sans-Serif Bold code points (each a surrogate pair),
    // the headline real-world case ("𝗙𝗟𝗨𝗫" instead of "FLUX").
    private static string ToMathBold(string ascii)
    {
        var sb = new StringBuilder();
        foreach (var c in ascii)
        {
            if (c >= 'A' && c <= 'Z')
                sb.Append(char.ConvertFromUtf32(0x1D5D4 + (c - 'A')));
            else if (c >= 'a' && c <= 'z')
                sb.Append(char.ConvertFromUtf32(0x1D5EE + (c - 'a')));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    [Fact]
    public void NormalizeWithMap_AlreadyAscii_ReturnsIdentityWithNullMap()
    {
        var (text, map) = UnicodeNormalizer.NormalizeWithMap("hi Alice there");

        text.Should().Be("hi Alice there");
        map.Should().BeNull();
    }

    [Fact]
    public void NormalizeWithMap_MathBold_FoldsToAsciiAndMapsBackToSurrogatePairs()
    {
        var original = ToMathBold("Alice"); // 5 code points, 10 UTF-16 units

        var (text, map) = UnicodeNormalizer.NormalizeWithMap(original);

        text.Should().Be("Alice");
        map.Should().Equal(0, 2, 4, 6, 8, 10);
        map![^1].Should().Be(original.Length);
    }

    [Fact]
    public void NormalizeWithMap_MapTranslatesMatchSpanToOriginalSubstring()
    {
        var prefix = "hi ";
        var name = ToMathBold("Alice");
        var original = prefix + name + " there";

        var (text, map) = UnicodeNormalizer.NormalizeWithMap(original);

        var start = text.IndexOf("Alice");
        var end = start + "Alice".Length;
        var origStart = map![start];
        var origEnd = map[end];

        original.Substring(origStart, origEnd - origStart).Should().Be(name);
    }

    [Fact]
    public void Normalize_MathBold_FoldsToAscii()
    {
        UnicodeNormalizer.Normalize(ToMathBold("FLUX")).Should().Be("FLUX");
    }

    [Theory]
    [InlineData("FLUX")]
    [InlineData("")]
    [InlineData("Khit'to")]
    public void Normalize_AlreadyPlain_ReturnsUnchanged(string value)
    {
        UnicodeNormalizer.Normalize(value).Should().Be(value);
    }

    [Fact]
    public void NormalizeWithMap_UnpairedSurrogate_DoesNotThrow()
    {
        var loneHighSurrogate = "\uD83D"; // high surrogate with no following low surrogate

        var act = () => UnicodeNormalizer.NormalizeWithMap(loneHighSurrogate);

        act.Should().NotThrow();
    }
}
