namespace MessageBrokerRabbitMq
{
    public class RmqConnectionConfig
    {
        public string Username { get; set; } = string.Empty;
        public string Pw { get; set; } = string.Empty;
        public int Port { get; set; }
        public int? Retrycount { get; set; }
        public string Host { get; set; } = string.Empty;
        public string Vhost { get; set; } = string.Empty;
        public string Exchange { get; set; } = string.Empty;
        public string DelayExchange { get; set; } = string.Empty;
    }

    public class PublicRmqConnectionConfiguration
    {
        public string Exchange { get; set; }
        public int RetryCount { get; set; }

        public PublicRmqConnectionConfiguration(string exchange, int? retryCount)
        {
            Exchange = exchange;
            RetryCount = retryCount ?? 5;
        }
    }
}
