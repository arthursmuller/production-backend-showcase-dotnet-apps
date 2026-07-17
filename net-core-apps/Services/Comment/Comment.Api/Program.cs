using InspacyApiHost;

ApiBuilder.New(new ApiOptions
{
    AppName = "Comment",
    WithInfra = true,
    WithApplication = true,
    WithCqrs = new(),
    WithQueues = MessageBrokers.RabbitMQ,
    WithControllers = true,
    WithRouteGroups = true,
});
