using CommomStartupExtensions;
using InspacyApiHost;

ApiBuilder.New(new ApiOptions
{
    AppName = "Following",
    AuthPolicies = [AuthPolicies.FromReactApp, AuthPolicies.ClientReadAccess],
    WithApplication = true,
    WithInfra = true,
    WithCqrs = new (),
    WithQueues = MessageBrokers.RabbitMQ,
});
