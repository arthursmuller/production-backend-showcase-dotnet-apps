using Microsoft.Extensions.Options;
using Moderation.Application.Features.Privacy;
using Moderation.Application.Options;

namespace Moderation.UnitTests;

public sealed class ModerationPrivacyTests
{
    [Fact]
    public void Subject_lookup_is_keyed_canonical_and_domain_separated()
    {
        var pseudonymizer = ModerationTestFactory.CreateSubjectPseudonymizer();

        var canonical = pseudonymizer.Create(42);

        Assert.Equal(ModerationSubjectPseudonymizer.EncodedLength, canonical.Length);
        Assert.Equal(canonical, pseudonymizer.Create("00042"));
        Assert.NotEqual(canonical, pseudonymizer.Create(43));
        Assert.DoesNotContain("42", canonical, StringComparison.Ordinal);

        var anotherKey = new ModerationSubjectPseudonymizer(
            Options.Create(new ModerationPrivacyOptions
            {
                SubjectLookupKey = Convert.ToBase64String(
                    Enumerable.Repeat((byte)0xa5, 32).ToArray()),
            }));
        Assert.NotEqual(canonical, anotherKey.Create(42));
    }

    [Fact]
    public void Subject_lookup_rejects_missing_or_short_keys()
    {
        Assert.False(ModerationPrivacyOptions.Validate(new ModerationPrivacyOptions()));
        Assert.Throws<InvalidOperationException>(() =>
            new ModerationSubjectPseudonymizer(
                Options.Create(new ModerationPrivacyOptions
                {
                    SubjectLookupKey = Convert.ToBase64String(new byte[16]),
                })));
    }
}
