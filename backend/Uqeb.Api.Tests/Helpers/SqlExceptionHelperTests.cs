using Microsoft.EntityFrameworkCore;
using Uqeb.Api.Helpers;
using Xunit;

namespace Uqeb.Api.Tests.Helpers;

public class SqlExceptionHelperTests
{
    [Fact]
    public void ShouldRetryReportNumberAllocation_ReturnsFalse_ForUnrelatedExceptions()
    {
        Assert.False(SqlExceptionHelper.ShouldRetryReportNumberAllocation(new InvalidOperationException("network")));
        Assert.False(SqlExceptionHelper.ShouldRetryReportNumberAllocation(
            new DbUpdateException("fk violation", new InvalidOperationException("fk"))));
    }

    [Fact]
    public void ShouldRetryReportNumberAllocation_ReturnsTrue_ForConcurrencyException()
    {
        Assert.True(SqlExceptionHelper.ShouldRetryReportNumberAllocation(new DbUpdateConcurrencyException()));
    }

    [Fact]
    public void ShouldRetryReportNumberAllocation_PreservesOriginalException_OnNonRetryableFailure()
    {
        var original = new InvalidOperationException("permission denied");
        Exception? caught = null;
        try
        {
            if (SqlExceptionHelper.ShouldRetryReportNumberAllocation(original))
                throw new InvalidOperationException("should not retry");
            throw original;
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.Same(original, caught);
    }

    [Fact]
    public void ShouldFallbackReportNumberAllocationToEf_ReturnsTrue_ForInvalidOperationException()
    {
        Assert.True(SqlExceptionHelper.ShouldFallbackReportNumberAllocationToEf(new InvalidOperationException("invalid object")));
    }

    [Fact]
    public void IsDuplicateKey_ReturnsFalse_WhenInnerExceptionIsNotSqlException()
    {
        var ex = new DbUpdateException("failed", new InvalidOperationException("not sql"));
        Assert.False(SqlExceptionHelper.IsDuplicateKey(ex));
        Assert.False(SqlExceptionHelper.IsDeadlock(ex));
    }
}
