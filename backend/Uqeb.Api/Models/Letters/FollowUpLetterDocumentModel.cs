namespace Uqeb.Api.Models.Letters;

public class FollowUpLetterDocumentModel
{
    public int TransactionId { get; set; }
    public int? TemplateId { get; set; }
    public string? LogoPath { get; set; }
    public string LetterNumber { get; set; } = string.Empty;
    public string GregorianDate { get; set; } = string.Empty;
    public string HijriDate { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string SenderDepartment { get; set; } = string.Empty;
    public int FollowUpSequence { get; set; }
    public string FollowUpSequenceText { get; set; } = string.Empty;
    public int? ResponseDeadlineDays { get; set; }
    public string Footer { get; set; } = string.Empty;
    public string Title { get; set; } = "خطاب تعقيب";
}
