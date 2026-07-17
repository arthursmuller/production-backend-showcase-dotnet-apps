namespace StorageProvider;

public class DownloadDto
{
    public string? Namespace { get; set; }
    public string? FileName { get; set; }
    public string? MimeType { get; set; }
    public string? Extension { get; set; }
    public byte[]? Blob { get; set; }

    public DownloadDto() { }

    public DownloadDto(string? nameSpace, string? fileName, string? mimeType, string? extension, byte[]? blob)
    {
        FileName = fileName;
        Namespace = nameSpace;
        MimeType = mimeType;
        Extension = extension;
        Blob = blob;
    }
}
