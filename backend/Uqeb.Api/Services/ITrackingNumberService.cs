namespace Uqeb.Api.Services;

public interface ITrackingNumberService
{
    Task<string> GenerateNextAsync(CancellationToken cancellationToken = default);
}
