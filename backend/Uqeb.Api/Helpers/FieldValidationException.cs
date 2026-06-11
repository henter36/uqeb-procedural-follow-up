namespace Uqeb.Api.Helpers;

public class FieldValidationException : Exception
{
    public Dictionary<string, string> FieldErrors { get; }

    public FieldValidationException(Dictionary<string, string> fieldErrors)
        : base("يرجى إكمال الحقول المطلوبة")
    {
        FieldErrors = fieldErrors;
    }
}
