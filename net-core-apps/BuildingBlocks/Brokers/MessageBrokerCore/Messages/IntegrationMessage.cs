using Newtonsoft.Json;

namespace MessageBrokerCore.Messages
{

    public class IntegrationMessage
    {
        public IntegrationMessage()
        {
            Id = Guid.NewGuid();
            CreationDate = DateTime.Now;
        }

        [JsonConstructor]
        public IntegrationMessage(Guid id, DateTime createDate)
        {
            Id = id;
            CreationDate = createDate;
        }

        [JsonConstructor]
        public IntegrationMessage(Guid id, DateTime createDate, DateTime scheduleDate)
        {
            Id = id;
            CreationDate = createDate;
            ScheduleDate = scheduleDate;
        }
        [JsonConstructor]
        public IntegrationMessage(string externalEntityId, string successRoutingKey, string successExchange)
        {
            Id = Guid.NewGuid();
            SuccessRoutingKey = successRoutingKey;
            SuccessExchange = successExchange;
            ExternalEntityId = externalEntityId;
            CreationDate = DateTime.Now;
        }
        [JsonConstructor]
        public IntegrationMessage(string externalEntityId)
        {
            Id = Guid.NewGuid();
            ExternalEntityId = externalEntityId;
            CreationDate = DateTime.Now;
        }

        [JsonProperty]
        public Guid? Id { get; set; }
        [JsonProperty]
        public DateTime CreationDate { get; private set; }
        [JsonProperty]
        public DateTime? ScheduleDate { get; set; }
        [JsonProperty]
        public string? SuccessRoutingKey { get; set; }
        [JsonProperty]
        public string? SuccessExchange { get; set; }
        [JsonProperty]
        public string? FailureRoutingKey { get; set; }
        [JsonProperty]
        public string? SuccessApiUrl { get; set; }
        [JsonProperty]
        public string? FailureApiUrl { get; set; }
        [JsonProperty]
        public string? ExternalEntityId { get; set; }
    }
}
