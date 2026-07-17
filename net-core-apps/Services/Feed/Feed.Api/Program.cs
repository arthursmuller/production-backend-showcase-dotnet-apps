using InspacyApiHost;

ApiBuilder.New(new ApiOptions
{
    AppName = "Feed",
    WithApplication = true,
    WithInfra = true,
    WithCqrs = new (),
    WithQueues = MessageBrokers.RabbitMQ,
});
