namespace CategoriesProvider.Providers.Api
{
    public class DefaultResponse<T>
    {
        public DefaultResponse() { }

        public DefaultResponse(T data)
        {
            Succeeded = true;
            Message = string.Empty;
            Errors = null;
            Data = data;
        }

        public T Data { get; set; }
        public bool Succeeded { get; set; }
        public string[] Errors { get; set; }
        public string Message { get; set; }
        public int StatusCode { get; set; }
    }
}
