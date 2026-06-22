namespace Uqeb.Api.Helpers;

public class EmptyReferenceNameException : InvalidOperationException
{
    public EmptyReferenceNameException(string message) : base(message) { }
}
