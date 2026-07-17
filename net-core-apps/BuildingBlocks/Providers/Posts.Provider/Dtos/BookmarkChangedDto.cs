namespace Posts.Provider.Dtos
{
    public class BookmarkChangedDto
    {
        public string IdOfRelatedEntity { get; set; } = string.Empty;
        public string TypeOfRelatedEntity { get; set; } = string.Empty;
        public int OwnerUserId { get; set; }
        public long GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public bool Created { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }
}
