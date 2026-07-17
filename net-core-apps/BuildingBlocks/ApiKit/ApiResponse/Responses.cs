public class PagedResponse<T> : Response<T>
{
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public long TotalRecords { get; set; }

    public PagedResponse(T data, int pageNumber, int pageSize)
    {
        PageNumber = pageNumber;
        PageSize = pageSize;
        Data = data;
        Message = null;
        Succeeded = true;
        Errors = null;
    }
    public PagedResponse(int pageNumber, int pageSize)
    {
        PageNumber = pageNumber;
        PageSize = pageSize;
        Message = null;
        Succeeded = true;
        Errors = null;
    }
}

public class Response<T>
{
    public Response()
    {
    }
    public Response(T data)
    {
        Succeeded = true;
        Message = null;
        Errors = null;
        Data = data;
    }

    public T? Data { get; set; }
    public bool? Succeeded { get; set; }
    public string[]? Errors { get; set; }
    public string? Message { get; set; }
}

public static class R
{
    public static Response<T> Success<T>(T data) => new(data);
}


