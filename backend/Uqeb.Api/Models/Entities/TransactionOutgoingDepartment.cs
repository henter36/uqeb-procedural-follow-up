namespace Uqeb.Api.Models.Entities;

public class TransactionOutgoingDepartment
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public int DepartmentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedById { get; set; }

    public Transaction Transaction { get; set; } = null!;
    public Department Department { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
}
