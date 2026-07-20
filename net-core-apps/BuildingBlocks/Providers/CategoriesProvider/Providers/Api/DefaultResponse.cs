namespace CategoriesProvider.Providers.Api
{
    public class DefaultResponse<T>
    {
        public T? Data { get; set; }
        public bool Succeeded { get; set; }
        public string[] Errors { get; set; } = [];
    }
}
