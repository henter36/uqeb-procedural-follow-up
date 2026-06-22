namespace Uqeb.Api.Helpers;

public class DuplicateReferenceException : Exception
{
    public DuplicateReferenceException(string message) : base(message) { }
}
