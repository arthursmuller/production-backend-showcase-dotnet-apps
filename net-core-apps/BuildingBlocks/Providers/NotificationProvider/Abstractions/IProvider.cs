using NotificationProvider.Dtos;

namespace NotificationProvider.Abstractions;
public interface IBroker
{
    Guid AddNotification(
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
        List<RecipientDto>? recipients = null);
}
