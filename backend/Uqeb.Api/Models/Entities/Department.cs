namespace Uqeb.Api.Models.Entities;

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameNormalized { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
    public ICollection<TransactionOutgoingDepartment> OutgoingTransactions { get; set; } = new List<TransactionOutgoingDepartment>();
    public ICollection<FollowUpDepartment> FollowUpDepartments { get; set; } = new List<FollowUpDepartment>();
    public ICollection<Transaction> IncomingTransactions { get; set; } = new List<Transaction>();
}
