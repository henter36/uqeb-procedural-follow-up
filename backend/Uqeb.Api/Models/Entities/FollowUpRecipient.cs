namespace Uqeb.Api.Models.Entities;

public class FollowUpRecipient
{
    public int Id { get; set; }
    public int FollowUpId { get; set; }
    public int ExternalPartyId { get; set; }

    public FollowUp FollowUp { get; set; } = null!;
    public ExternalParty ExternalParty { get; set; } = null!;
}
