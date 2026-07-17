using MessageBrokerCore.Messages;
using Newtonsoft.Json;
using NotificationProvider.Dtos;
using System.Collections.Generic;

namespace NotificationProvider.Providers.MessageBroker.Messages
{
    public class AddNotificationMessage : IntegrationMessage
    {

        [JsonProperty]
        public string? BusinessId { get; set; }
        [JsonProperty]
        public string? UserId { get; set; }
        [JsonProperty]
        public string? ReplacementUserId { get; set; }
        [JsonProperty]
        public string? SourceUserId { get; set; }
        [JsonProperty]
        public string? SourceEntityType { get; set; }
        [JsonProperty]
        public string? SourceEntityId { get; set; }
        [JsonProperty]
        public string? Title { get; set; }
        [JsonProperty]
        public string? Message { get; set; }
        [JsonProperty]
        public string? Description { get; set; }
        [JsonProperty]
        public string? TemplateFileName { get; set; }
        [JsonProperty]
        public string? Template { get; set; }
        [JsonProperty]
        public IDictionary<string, string>? Replacements { get; set; }
        [JsonProperty]
        public List<int>? ChannelIds { get; set; }
        [JsonProperty]
        public List<RecipientDto>? Recipients { get; set; }

        public AddNotificationMessage(
            string? businessId = null,
            string? userId = null,
            string? sourceUserId = null,
            string? replacementUserId = null,
            string? sourceEntityType = null,
            string? sourceEntityId = null,
            string? title = null,
            string? message = null,
            string? description = null,
            string? templateFileName = null,
            string? template = null,
            IDictionary<string, string>? replacements = null,
            List<int>? channelIds = null,
            List<RecipientDto>? recipients = null)
        {
            BusinessId = businessId;
            UserId = userId;
            SourceUserId = sourceUserId;
            ReplacementUserId = replacementUserId;
            SourceEntityType = sourceEntityType;
            SourceEntityId = sourceEntityId;
            Title = title;
            Message = message;
            Description = description;
            TemplateFileName = templateFileName;
            Template = template;
            Replacements = replacements;
            ChannelIds = channelIds;
            Recipients = recipients;
        }
    }
}
