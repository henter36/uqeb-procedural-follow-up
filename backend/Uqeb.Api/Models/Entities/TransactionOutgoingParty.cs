namespace Uqeb.Api.Models.Entities;

public class TransactionOutgoingParty
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public int ExternalPartyId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedById { get; set; }

    public Transaction Transaction { get; set; } = null!;
    public ExternalParty ExternalParty { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
}
