using System.Text;
using BuildingBlocks.Security;
using Microsoft.Extensions.Options;

namespace Interaction.Infraestructure.Configuration;

public sealed class InteractionPrivacyOptionsValidator
    : IValidateOptions<InteractionPrivacyOptions>
{
    public ValidateOptionsResult Validate(string? name, InteractionPrivacyOptions value) =>
        HmacSha256.IsValidKey(Encoding.UTF8.GetBytes(value.ViewHashKey ?? string.Empty))
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "Privacy:ViewHashKey must resolve to at least 32 bytes.");
}
