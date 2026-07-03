namespace Uqeb.Api.Models.Entities;

public class RecurringTransactionTemplateDepartment
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public int DepartmentId { get; set; }
    public int? SortOrder { get; set; }

    public RecurringTransactionTemplate Template { get; set; } = null!;
    public Department Department { get; set; } = null!;
}
