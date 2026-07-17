using MessageBrokerCore.Messages;
using Posts.Provider.Dtos;

namespace Posts.Provider
{
    public class BookmarkChangedMessage : IntegrationMessage
    {
        public BookmarkChangedDto Dto { get; set; }
        public BookmarkChangedMessage(BookmarkChangedDto dto) : base() { Dto = dto; }
    }

}
