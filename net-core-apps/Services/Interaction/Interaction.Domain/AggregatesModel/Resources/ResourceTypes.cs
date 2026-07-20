namespace Interaction.Domain.AggregatesModel;

public enum ResourceType : byte
{
    Comment = 1,
    Post = 2,
}

public enum ResourceLifecycle : byte
{
    Unknown = 0,
    Active = 1,
    Deleted = 2,
}

public enum ResourceVisibility : byte
{
    Private = 0,
    Public = 1,
    Restricted = 2,
}
