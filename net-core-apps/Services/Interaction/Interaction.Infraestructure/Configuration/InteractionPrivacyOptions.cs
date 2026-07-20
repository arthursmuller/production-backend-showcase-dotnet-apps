namespace Interaction.Infraestructure.Configuration;

public sealed class InteractionPrivacyOptions
{
    public const string SectionName = "Privacy";
    public string ViewHashKey { get; set; } = string.Empty;
}
