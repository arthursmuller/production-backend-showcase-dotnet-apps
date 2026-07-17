using RichEnumeration;

namespace NotificationProvider.Dtos
{
    public class NotificationChannel : Enumeration
    {
        public static NotificationChannel Platform = new NotificationChannel(1, nameof(Platform).ToLowerInvariant());

        public NotificationChannel(int id, string name): base(id, name) { }
    }
    public class NotificationMessageReplacements : Enumeration
    {
        public static NotificationMessageReplacements UserName = new NotificationMessageReplacements(1, "{" + nameof(UserName).ToLowerInvariant() + "}");

        public NotificationMessageReplacements(int id, string name) : base(id, name) { }
    }
}
