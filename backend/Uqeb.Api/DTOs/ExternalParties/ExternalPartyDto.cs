namespace Uqeb.Api.DTOs.ExternalParties;

public class ExternalPartyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? ContactInfo { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateExternalPartyRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? ContactInfo { get; set; }
}

public class UpdateExternalPartyRequest
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? ContactInfo { get; set; }
    public bool? IsActive { get; set; }
}
