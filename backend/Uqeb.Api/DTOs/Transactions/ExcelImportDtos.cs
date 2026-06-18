namespace Uqeb.Api.DTOs.Transactions;

public class ExcelImportPreviewRowDataDto
{
    public string IncomingNumber { get; set; } = string.Empty;
    public DateTime? IncomingDate { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string AssignedDepartmentName { get; set; } = string.Empty;
    public string? ActionTaken { get; set; }
    public bool WillCompleteResponse { get; set; }
}

public class ExcelImportPreviewRowDto
{
    public int RowNumber { get; set; }
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public ExcelImportPreviewRowDataDto? Data { get; set; }
}

public class ExcelImportPreviewResultDto
{
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public List<ExcelImportPreviewRowDto> Rows { get; set; } = new();
}

public class ExcelImportRejectedRowDto
{
    public int RowNumber { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class ExcelImportCommitResultDto
{
    public int ImportedCount { get; set; }
    public int RejectedCount { get; set; }
    public List<int> ImportedTransactionIds { get; set; } = new();
    public List<ExcelImportRejectedRowDto> RejectedRows { get; set; } = new();
}
