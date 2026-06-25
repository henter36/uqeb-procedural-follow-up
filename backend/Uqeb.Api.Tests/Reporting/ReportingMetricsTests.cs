using Uqeb.Api.Reporting.Operations;
using Xunit;

namespace Uqeb.Api.Tests.Reporting;

public class ReportingMetricsTests
{
    [Theory]
    [InlineData(-5, "0")]
    [InlineData(0, "0")]
    [InlineData(1, "1-100")]
    [InlineData(100, "1-100")]
    [InlineData(101, "101-1000")]
    [InlineData(1000, "101-1000")]
    [InlineData(1001, "1001-5000")]
    [InlineData(5000, "1001-5000")]
    [InlineData(5001, "5001+")]
    public void BucketSnapshotCount_UsesStableBuckets(int snapshotCount, string expectedBucket) =>
        Assert.Equal(expectedBucket, ReportingMetrics.BucketSnapshotCount(snapshotCount));
}
