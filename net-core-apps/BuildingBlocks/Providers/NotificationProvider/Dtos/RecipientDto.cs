namespace NotificationProvider.Dtos
{
    public class RecipientDto
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Cellphone { get; set; }
        public RecipientDto() { }
        public RecipientDto(string? name, string? email, string? cellphone) 
        {
            Name = name;
            Email = email;
            Cellphone = cellphone;
        }
    }
}
