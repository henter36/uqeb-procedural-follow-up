namespace Uqeb.Api.Reporting.DataQuality;

public interface IDataQualityService
{
    Task<DataQualitySummaryDto> GetSummaryAsync(DataQualityQueryDto query, CancellationToken ct = default);
}
