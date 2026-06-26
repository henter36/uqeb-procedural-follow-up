namespace Uqeb.Api.Helpers;

public static class FollowUpSequenceCalculator
{
    public static int CalculateExpectedSequence(int registeredFollowUpCount) => registeredFollowUpCount + 1;

    public static string ToArabicText(int sequence) => sequence switch
    {
        1 => "التعقيب الأول",
        2 => "التعقيب الثاني",
        3 => "التعقيب الثالث",
        _ => $"التعقيب رقم {sequence}",
    };
}
