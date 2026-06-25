namespace Uqeb.Api.Helpers;

public sealed class InvalidTransactionSearchCursorException : Exception
{
    public InvalidTransactionSearchCursorException(string message) : base(message)
    {
    }
}
