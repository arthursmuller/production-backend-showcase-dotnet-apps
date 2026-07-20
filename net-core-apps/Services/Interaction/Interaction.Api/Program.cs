using Interaction.Api;

await InspacyApiHost.ApiBuilder.NewAsync(new InspacyApiHost.ApiOptions
{
    AppName = "Interaction",
    ConfigureServices = builder =>
        builder.Services.AddInteractionAuthorization(builder.Configuration),
});
