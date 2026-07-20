using System.Text;
using BuildingBlocks.Security;
using Xunit;

namespace SecurityPrimitives.UnitTests;

public sealed class SecurityPrimitivesTests
{
    [Fact]
    public void Key_material_requires_a_bounded_base64_value_with_at_least_32_decoded_bytes()
    {
        var expected = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var encoded = Convert.ToBase64String(expected);

        Assert.True(Base64KeyMaterial.TryDecode(encoded, out var actual));
        Assert.Equal(expected, actual);
        Assert.False(Base64KeyMaterial.TryDecode(
            Convert.ToBase64String(expected[..31]),
            out var shortKey));
        Assert.Empty(shortKey);
        Assert.False(Base64KeyMaterial.TryDecode("not-base64", out var malformed));
        Assert.Empty(malformed);
        Assert.False(Base64KeyMaterial.TryDecode(
            new string('A', Base64KeyMaterial.MaximumEncodedKeyLength + 1),
            out var oversized));
        Assert.Empty(oversized);
    }

    [Fact]
    public void Key_material_can_preserve_legacy_whitespace_compatibility_explicitly()
    {
        var expected = Enumerable.Repeat((byte)0x2A, 32).ToArray();
        var encoded = $" {Convert.ToBase64String(expected)} ";

        Assert.False(Base64KeyMaterial.TryDecode(encoded, out _));
        Assert.True(Base64KeyMaterial.TryDecode(
            encoded,
            out var actual,
            rejectSurroundingWhitespace: false));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Base64Url_round_trips_without_padding_and_strict_decode_is_bounded()
    {
        byte[] expected = [0xFB, 0xEF, 0xFF, 0x00];

        var encoded = Base64Url.Encode(expected);

        Assert.Equal("--__AA", encoded);
        Assert.True(Base64Url.TryDecode(encoded, out var actual, encoded.Length));
        Assert.Equal(expected, actual);
        Assert.False(Base64Url.TryDecode(encoded, out _, encoded.Length - 1));
        Assert.False(Base64Url.TryDecode("++//", out _, 4));
        Assert.False(Base64Url.TryDecode("AB", out _, 2));
    }

    [Fact]
    public void Compatible_decode_accepts_the_standard_base64_alphabet_used_by_existing_tokens()
    {
        Assert.Equal(
            new byte[] { 0xFB, 0xEF, 0xFF },
            Base64Url.Decode("++//"));
        Assert.Equal(
            new byte[] { 0xFB, 0xEF, 0xFF },
            Base64Url.Decode("--__"));
    }

    [Fact]
    public void HmacSha256_matches_the_known_vector_and_fails_closed_on_tampering()
    {
        var key = Enumerable.Repeat((byte)0x0B, 32).ToArray();
        var payload = Encoding.ASCII.GetBytes("Hi There");

        var tag = HmacSha256.Protect(key, payload);

        Assert.Equal(
            "198a607eb44bfbc69903a0f1cf2bbdc5ba0aa3f3d9ae3c1c7a3b1696a0b68cf7",
            Convert.ToHexStringLower(tag));
        Assert.True(HmacSha256.Verify(key, payload, tag));
        Assert.False(HmacSha256.Verify(key, "Hi There!"u8, tag));
        Assert.Throws<ArgumentException>(() =>
            HmacSha256.Protect(new byte[31], payload));
    }
}
