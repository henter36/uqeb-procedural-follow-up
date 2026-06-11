namespace Uqeb.Api.Models.Entities;

public class FollowUpDepartment
{
    public int Id { get; set; }
    public int FollowUpId { get; set; }
    public int DepartmentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedById { get; set; }

    public FollowUp FollowUp { get; set; } = null!;
    public Department Department { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
}
