namespace Shopit.Application.Products.DTOs;

public class ImportResultDto
{
    public int AddedCount { get; set; }

    public int FailedCount { get; set; }

    public List<ImportErrorDto> Errors { get; set; } = new();
}

public class ImportErrorDto
{
    public int Row { get; set; }

    public string Reason { get; set; } = string.Empty;
}