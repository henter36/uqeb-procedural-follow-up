using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Helpers;
using Uqeb.Api.Models.Enums;

namespace Uqeb.Api.Services;

public interface IExcelTransactionImportService
{
    Task<ExcelImportPreviewResultDto> PreviewAsync(Stream fileStream, CancellationToken cancellationToken = default);
    Task<ExcelImportCommitResultDto> CommitAsync(Stream fileStream, ICurrentUserService currentUser, CancellationToken cancellationToken = default);
}

public class ExcelTransactionImportService : IExcelTransactionImportService
{
    private const string NotesDefault = "تم الاستيراد من ملف Excel";
    private const int DefaultResponseDueDays = 10;
    private const int MaxImportRows = 1000;
    private const string DefaultIncomingDepartmentName = "وارد مستورد";
    private const string DefaultCategoryName = "عام";
    private const string ExistingIncomingNumberError = "رقم الخطاب الوارد موجود مسبقاً في النظام";
    private const string UnexpectedRowImportError = "تعذر استيراد هذا الصف بسبب خطأ غير متوقع";
    private const string CorruptExcelFileError =
        "فشل قراءة ملف Excel. تأكد من أن الملف غير تالف وصيغته .xlsx صالحة.";
    private const string TooManyRowsError = "عدد الصفوف في الملف يتجاوز الحد المسموح للاستيراد";
    private const string EmptyExcelFileError = "الملف فارغ";

    private static readonly string IncomingSourceTypeInternal = IncomingSourceType.Internal.ToString();
    private static readonly string PriorityNormal = Priority.Normal.ToString();
    private static readonly string ResponseTypeExternal = ResponseType.External.ToString();

    private static readonly string[] RequiredHeaders =
    [
        "رقم الخطاب الوارد",
        "تاريخ الخطاب الوارد",
        "الموضوع",
        "الجهة المحال لها"
    ];

    private static readonly string[] ActionHeaderVariants =
    [
        "الإجراء المتخذ",
        "الاجراء المتخذ"
    ];

    private static readonly string[] SupportedGregorianDateFormats =
    [
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy"
    ];

    private readonly AppDbContext _db;
    private readonly ITransactionService _transactions;
    private readonly ILogger<ExcelTransactionImportService> _logger;

    public ExcelTransactionImportService(
        AppDbContext db,
        ITransactionService transactions,
        ILogger<ExcelTransactionImportService> logger)
    {
        _db = db;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task<ExcelImportPreviewResultDto> PreviewAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        var context = await BuildContextAsync(cancellationToken);
        var parseResult = ParseWorkbook(fileStream, context);

        if (parseResult.FileError != null)
            throw new InvalidOperationException(parseResult.FileError);

        await ApplyExistingIncomingNumberChecksAsync(parseResult.Rows, cancellationToken);

        return new ExcelImportPreviewResultDto
        {
            TotalRows = parseResult.Rows.Count,
            ValidRows = parseResult.Rows.Count(r => r.IsValid),
            InvalidRows = parseResult.Rows.Count(r => !r.IsValid),
            Rows = parseResult.Rows.Select(MapPreviewRow).ToList()
        };
    }

    public Task<ExcelImportCommitResultDto> CommitAsync(Stream fileStream, ICurrentUserService currentUser, CancellationToken cancellationToken = default) =>
        ProcessCommitAsync(fileStream, currentUser, cancellationToken);

    private async Task<ExcelImportCommitResultDto> ProcessCommitAsync(
        Stream fileStream,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        var context = await BuildContextAsync(cancellationToken);
        var parseResult = ParseWorkbook(fileStream, context);

        if (parseResult.FileError != null)
            throw new InvalidOperationException(parseResult.FileError);

        await ApplyExistingIncomingNumberChecksAsync(parseResult.Rows, cancellationToken);

        var result = new ExcelImportCommitResultDto
        {
            RejectedCount = parseResult.Rows.Count(r => !r.IsValid),
            RejectedRows = parseResult.Rows
                .Where(r => !r.IsValid)
                .Select(r => new ExcelImportRejectedRowDto { RowNumber = r.RowNumber, Errors = r.Errors })
                .ToList()
        };

        foreach (var row in parseResult.Rows.Where(r => r.IsValid))
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var created = await _transactions.CreateAsync(new CreateTransactionRequest
                {
                    IncomingNumber = row.IncomingNumber,
                    IncomingDate = row.IncomingDate,
                    Subject = row.Subject,
                    IncomingSourceType = IncomingSourceTypeInternal,
                    IncomingFromDepartmentId = context.DefaultIncomingDepartmentId,
                    Priority = PriorityNormal,
                    ResponseType = ResponseTypeExternal,
                    ResponseDueDays = DefaultResponseDueDays,
                    CategoryId = context.DefaultCategoryId,
                    Notes = NotesDefault,
                    OutgoingDepartmentIds = []
                }, currentUser.UserId);

                var assignment = await _transactions.AddAssignmentAsync(created.Id, new CreateAssignmentRequest
                {
                    DepartmentId = row.AssignedDepartmentId,
                    AssignedDate = row.IncomingDate,
                    RequiredAction = row.ActionTaken,
                    ReplyDueDays = DefaultResponseDueDays
                }, currentUser.UserId);

                if (row.WillCompleteResponse)
                {
                    var replySummary = string.IsNullOrWhiteSpace(row.ActionTaken)
                        ? "تم الرد عبر الاستيراد من Excel"
                        : row.ActionTaken.Trim();

                    await _transactions.ReplyAssignmentAsync(created.Id, assignment.Id, new ReplyAssignmentRequest
                    {
                        ReplyDate = row.IncomingDate,
                        ReplySummary = replySummary
                    }, currentUser);

                    await _transactions.CompleteResponseAsync(created.Id, new CompleteResponseRequest
                    {
                        ResponseDate = row.IncomingDate,
                        ResponseSummary = replySummary,
                        OutgoingNumber = row.IncomingNumber,
                        OutgoingDate = row.IncomingDate
                    }, currentUser);
                }

                await tx.CommitAsync(cancellationToken);
                result.ImportedTransactionIds.Add(created.Id);
            }
            catch (OperationCanceledException)
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
            catch (DuplicateIncomingNumberException)
            {
                await tx.RollbackAsync(cancellationToken);
                _db.ChangeTracker.Clear();
                result.RejectedCount++;
                result.RejectedRows.Add(new ExcelImportRejectedRowDto
                {
                    RowNumber = row.RowNumber,
                    Errors = [ExistingIncomingNumberError]
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(cancellationToken);
                _db.ChangeTracker.Clear();
                _logger.LogError(ex, "Failed to import Excel row {RowNumber}", row.RowNumber);
                result.RejectedCount++;
                result.RejectedRows.Add(new ExcelImportRejectedRowDto
                {
                    RowNumber = row.RowNumber,
                    Errors = [UnexpectedRowImportError]
                });
            }
        }

        result.ImportedCount = result.ImportedTransactionIds.Count;
        return result;
    }

    private static ExcelImportPreviewRowDto MapPreviewRow(ValidatedImportRow row) => new()
    {
        RowNumber = row.RowNumber,
        IsValid = row.IsValid,
        Errors = row.Errors,
        Data = new ExcelImportPreviewRowDataDto
        {
            IncomingNumber = row.IncomingNumber,
            IncomingDate = row.IncomingDate == default ? null : row.IncomingDate,
            Subject = row.Subject,
            AssignedDepartmentName = row.AssignedDepartmentName,
            ActionTaken = row.ActionTaken,
            WillCompleteResponse = row.WillCompleteResponse
        }
    };

    private async Task<ImportContext> BuildContextAsync(CancellationToken cancellationToken)
    {
        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        if (departments.Count == 0)
            throw new InvalidOperationException("لا توجد إدارات في النظام. لا يمكن الاستيراد.");

        var categories = await _db.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        if (categories.Count == 0)
            throw new InvalidOperationException("لا توجد تصنيفات في النظام. لا يمكن الاستيراد.");

        var defaultIncoming = departments.FirstOrDefault(d =>
                string.Equals(d.Name.Trim(), DefaultIncomingDepartmentName, StringComparison.OrdinalIgnoreCase))
            ?? departments[0];

        var defaultCategory = categories.FirstOrDefault(c =>
                string.Equals(c.Name.Trim(), DefaultCategoryName, StringComparison.OrdinalIgnoreCase))
            ?? categories[0];

        var departmentMap = departments
            .GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        return new ImportContext(
            defaultIncoming.Id,
            defaultCategory.Id,
            departmentMap);
    }

    private async Task ApplyExistingIncomingNumberChecksAsync(
        List<ValidatedImportRow> rows,
        CancellationToken cancellationToken)
    {
        var incomingNumbersInFile = rows
            .Select(r => r.IncomingNumber)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (incomingNumbersInFile.Count == 0)
            return;

        var existingInDb = await _db.Transactions
            .AsNoTracking()
            .Where(t => incomingNumbersInFile.Contains(t.IncomingNumber))
            .Select(t => t.IncomingNumber)
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existingInDb, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.IncomingNumber))
                continue;

            if (!existingSet.Contains(row.IncomingNumber))
                continue;

            if (!row.Errors.Contains(ExistingIncomingNumberError))
                row.Errors.Add(ExistingIncomingNumberError);

            row.IsValid = false;
        }
    }

    private static ParseResult ParseWorkbook(Stream fileStream, ImportContext context)
    {
        try
        {
            using var workbook = new XLWorkbook(fileStream);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet == null)
                return ParseResult.WithFileError(EmptyExcelFileError);

            var headerRow = worksheet.FirstRowUsed();
            if (headerRow == null)
                return ParseResult.WithFileError(EmptyExcelFileError);

            var columnMap = MapHeaders(headerRow);
            var missingHeaders = RequiredHeaders.Where(h => !columnMap.ContainsKey(h)).ToList();
            var hasActionColumn = ActionHeaderVariants.Any(columnMap.ContainsKey);
            if (missingHeaders.Count > 0 || !hasActionColumn)
            {
                var missing = missingHeaders.Concat(hasActionColumn ? [] : [ActionHeaderVariants[0]]).ToList();
                return ParseResult.WithFileError($"الملف لا يحتوي على الأعمدة المطلوبة: {string.Join("، ", missing)}");
            }

            var actionColumn = ActionHeaderVariants.First(columnMap.ContainsKey);
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();
            if (lastRow <= headerRow.RowNumber())
                return ParseResult.WithFileError(EmptyExcelFileError);

            var scanResult = ReadAndValidateRows(
                worksheet,
                headerRow,
                columnMap,
                actionColumn,
                context);

            if (scanResult.ExceededLimit)
                return ParseResult.WithFileError(TooManyRowsError);

            var rows = scanResult.Rows;

            if (rows.Count == 0)
                return ParseResult.WithFileError(EmptyExcelFileError);

            return new ParseResult(null, rows);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ParseResult.WithFileError(CorruptExcelFileError);
        }
    }

    private static RowScanResult ReadAndValidateRows(
        IXLWorksheet worksheet,
        IXLRow headerRow,
        IReadOnlyDictionary<string, int> columnMap,
        string actionColumn,
        ImportContext context)
    {
        var rows = new List<ValidatedImportRow>();
        var fileIncomingNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();

        for (var rowNumber = headerRow.RowNumber() + 1; rowNumber <= lastRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (IsEmptyDataRow(row, columnMap, actionColumn))
                continue;

            if (rows.Count >= MaxImportRows)
                return new RowScanResult(rows, true);

            var validated = ValidateRow(
                rowNumber,
                row,
                columnMap,
                actionColumn,
                context,
                fileIncomingNumbers);

            rows.Add(validated);

            if (validated.IsValid)
                fileIncomingNumbers.Add(validated.IncomingNumber);
        }

        return new RowScanResult(rows, false);
    }

    private static Dictionary<string, int> MapHeaders(IXLRow headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var header = NormalizeHeader(cell.GetString());
            if (string.IsNullOrEmpty(header))
                continue;

            map[header] = cell.Address.ColumnNumber;
        }

        return map;
    }

    private static string NormalizeHeader(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static bool IsEmptyDataRow(IXLRow row, IReadOnlyDictionary<string, int> columnMap, string actionColumn)
    {
        var values = RequiredHeaders
            .Select(h => GetCellText(row.Cell(columnMap[h])))
            .Append(GetCellText(row.Cell(columnMap[actionColumn])))
            .Select(v => v.Trim());

        return values.All(string.IsNullOrWhiteSpace);
    }

    private static ValidatedImportRow ValidateRow(
        int rowNumber,
        IXLRow row,
        IReadOnlyDictionary<string, int> columnMap,
        string actionColumn,
        ImportContext context,
        HashSet<string> fileIncomingNumbers)
    {
        var errors = new List<string>();

        var incomingNumber = GetCellText(row.Cell(columnMap["رقم الخطاب الوارد"])).Trim();
        var subject = GetCellText(row.Cell(columnMap["الموضوع"])).Trim();
        var assignedDepartmentName = GetCellText(row.Cell(columnMap["الجهة المحال لها"])).Trim();
        var actionTaken = GetCellText(row.Cell(columnMap[actionColumn])).Trim();
        var incomingDateCell = row.Cell(columnMap["تاريخ الخطاب الوارد"]);

        if (string.IsNullOrWhiteSpace(incomingNumber))
            errors.Add("رقم الخطاب الوارد مطلوب");

        DateTime incomingDate = default;
        if (incomingDateCell.IsEmpty())
            errors.Add("تاريخ الخطاب الوارد مطلوب");
        else if (!TryParseDateCell(incomingDateCell, out incomingDate))
            errors.Add("تاريخ الخطاب الوارد غير صالح");

        if (string.IsNullOrWhiteSpace(subject))
            errors.Add("الموضوع مطلوب");

        if (string.IsNullOrWhiteSpace(assignedDepartmentName))
            errors.Add("الجهة المحال لها مطلوبة");

        int assignedDepartmentId = 0;
        if (!string.IsNullOrWhiteSpace(assignedDepartmentName)
            && !context.DepartmentNameToId.TryGetValue(assignedDepartmentName.Trim(), out assignedDepartmentId))
        {
            errors.Add($"الإدارة غير موجودة: {assignedDepartmentName}");
        }

        if (!string.IsNullOrWhiteSpace(incomingNumber) && fileIncomingNumbers.Contains(incomingNumber))
            errors.Add("رقم الخطاب الوارد مكرر في الملف");

        var willCompleteResponse = ShouldCompleteResponse(actionTaken);
        var isValid = errors.Count == 0;

        return new ValidatedImportRow
        {
            RowNumber = rowNumber,
            IsValid = isValid,
            Errors = errors,
            IncomingNumber = incomingNumber,
            IncomingDate = incomingDate,
            Subject = subject,
            AssignedDepartmentId = assignedDepartmentId,
            AssignedDepartmentName = assignedDepartmentName,
            ActionTaken = string.IsNullOrWhiteSpace(actionTaken) ? null : actionTaken,
            WillCompleteResponse = willCompleteResponse
        };
    }

    private static bool ShouldCompleteResponse(string? actionTaken)
    {
        if (string.IsNullOrWhiteSpace(actionTaken))
            return false;

        return actionTaken.Contains("تسجيل الإفادة", StringComparison.OrdinalIgnoreCase)
            || actionTaken.Contains("تسجيل الافادة", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCellText(IXLCell cell) =>
        cell.IsEmpty() ? string.Empty : cell.GetFormattedString().Trim();

    private static bool TryParseDateCell(IXLCell cell, out DateTime date)
    {
        if (cell.IsEmpty())
        {
            date = default;
            return false;
        }

        if (cell.DataType == XLDataType.DateTime)
        {
            date = cell.GetDateTime().Date;
            return true;
        }

        if (cell.TryGetValue(out DateTime dateTime))
        {
            date = dateTime.Date;
            return true;
        }

        if (cell.TryGetValue(out double serial))
        {
            try
            {
                date = DateTime.FromOADate(serial).Date;
                return true;
            }
            catch
            {
                // fall through to text parsing
            }
        }

        var text = cell.GetString().Trim();
        if (DateTime.TryParseExact(
                text,
                SupportedGregorianDateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
        {
            date = date.Date;
            return true;
        }

        date = default;
        return false;
    }

    private sealed record RowScanResult(
        List<ValidatedImportRow> Rows,
        bool ExceededLimit);

    private sealed record ImportContext(
        int DefaultIncomingDepartmentId,
        int DefaultCategoryId,
        Dictionary<string, int> DepartmentNameToId);

    private sealed class ParseResult
    {
        public string? FileError { get; }
        public List<ValidatedImportRow> Rows { get; }

        public ParseResult(string? fileError, List<ValidatedImportRow> rows)
        {
            FileError = fileError;
            Rows = rows;
        }

        public static ParseResult WithFileError(string message) =>
            new(message, []);
    }

    private sealed class ValidatedImportRow
    {
        public int RowNumber { get; init; }
        public bool IsValid { get; set; }
        public List<string> Errors { get; init; } = [];
        public string IncomingNumber { get; init; } = string.Empty;
        public DateTime IncomingDate { get; init; }
        public string Subject { get; init; } = string.Empty;
        public int AssignedDepartmentId { get; init; }
        public string AssignedDepartmentName { get; init; } = string.Empty;
        public string? ActionTaken { get; init; }
        public bool WillCompleteResponse { get; init; }
    }
}
