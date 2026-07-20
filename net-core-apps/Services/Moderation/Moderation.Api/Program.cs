using Moderation.API;

await InspacyApiHost.ApiBuilder.NewAsync(new InspacyApiHost.ApiOptions
{
    AppName = "Moderation",
    AuthPolicies = [],
    ConfigureServices = builder =>
        builder.Services.AddModerationAuthorization(builder.Configuration),
});
