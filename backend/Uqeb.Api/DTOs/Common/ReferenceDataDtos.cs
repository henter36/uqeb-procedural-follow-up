namespace Uqeb.Api.DTOs.Common;

public class ReferenceDataListRequest
{
    public string? Search { get; set; }
    /// <summary>all | active | inactive</summary>
    public string? Status { get; set; }
    public string SortBy { get; set; } = "name";
    public bool SortDesc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class LookupRequest
{
    public string? Search { get; set; }
    public bool ActiveOnly { get; set; } = true;
    public int Limit { get; set; } = 50;
}

public class LookupItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? SubLabel { get; set; }
}
