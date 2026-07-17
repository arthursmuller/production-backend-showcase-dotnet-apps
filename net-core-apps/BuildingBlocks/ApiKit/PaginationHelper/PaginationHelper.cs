public static class PaginationFactory
{
    public static PagedResponse<IEnumerable<T>> CreatePagedReponse<T>(IEnumerable<T> pagedData, int pageNumber, int pageSize, long totalRecords)
    {
        var response = new PagedResponse<IEnumerable<T>>(pagedData, pageNumber, pageSize);
        var totalPages = (double)totalRecords / pageSize;
        int roundedTotalPages = Convert.ToInt32(Math.Ceiling(totalPages));
        response.TotalPages = roundedTotalPages;
        response.TotalRecords = totalRecords;

        return response;
    }

    public static PagedResponse<IEnumerable<T>> CreatePagedReponse<T>(
        (IEnumerable<T> pagedData, long totalRecords) tupleData,
        int? pageNumber,
        int? pageSize)
    {
        int size = pageSize ?? 10;
        int number = pageNumber ?? 1;

        // Access the fields via the parameter name
        var response = new PagedResponse<IEnumerable<T>>(tupleData.pagedData, number, size);

        // Calculate total pages safely
        var totalPages = (double)tupleData.totalRecords / size;
        int roundedTotalPages = Convert.ToInt32(Math.Ceiling(totalPages));

        response.TotalPages = roundedTotalPages;
        response.TotalRecords = tupleData.totalRecords;

        return response;
    }

    public static PagedResponse<bool> CreatePagedReponse(int pageNumber, int pageSize, long totalRecords)
    {
        var response = new PagedResponse<bool>(pageNumber, pageSize);
        var totalPages = (double)totalRecords / pageSize;
        int roundedTotalPages = Convert.ToInt32(Math.Ceiling(totalPages));
        response.TotalPages = roundedTotalPages;
        response.TotalRecords = totalRecords;

        return response;
    }
}

public class CommomFilter
{
    public string? SortBy { get; set; } = "date_desc";
    public string? Search { get; set; } = "";
    public int? PageSize { get; set; }
    public int? PageNumber { get; set; }
    public DateTime?[]? DateRange { get; set; } = null;

    public CommomFilter() { }
}