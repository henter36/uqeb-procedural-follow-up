using System.Net;
using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Uqeb.Api.Data;
using Uqeb.Api.DTOs.Security;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class SecurityAuditServiceCancellationTests
{
    private sealed class CapturingCommandInterceptor : DbCommandInterceptor
    {
        public CancellationToken? LastReaderCancellationToken { get; private set; }
        public CancellationToken? LastNonQueryCancellationToken { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            LastReaderCancellationToken = cancellationToken;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            LastNonQueryCancellationToken = cancellationToken;
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class ThrowingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public Exception? ExceptionToThrow { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is { } exception)
                throw exception;

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed record TestContext(
        SqliteConnection Connection,
        AppDbContext Db,
        CapturingCommandInterceptor CommandInterceptor,
        ThrowingSaveChangesInterceptor SaveChangesInterceptor) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private static async Task<TestContext> CreateContextAsync()
    {
        var commandInterceptor = new CapturingCommandInterceptor();
        var saveChangesInterceptor = new ThrowingSaveChangesInterceptor();
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(commandInterceptor, saveChangesInterceptor)
            .Options;

        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        return new TestContext(connection, db, commandInterceptor, saveChangesInterceptor);
    }

    private static DefaultHttpContext CreateHttpContext(CancellationToken requestAborted = default)
    {
        var context = new DefaultHttpContext
        {
            RequestAborted = requestAborted
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        context.Request.Headers["User-Agent"] = "xunit-agent";
        return context;
    }

    [Fact]
    public async Task GetRecentLoginAttemptsAsync_propagates_caller_supplied_cancellation_token_to_the_database_call()
    {
        await using var context = await CreateContextAsync();
        var service = new SecurityAuditService(context.Db);
        using var cts = new CancellationTokenSource();

        await service.GetRecentLoginAttemptsAsync(new LoginAttemptFilterRequest(), cts.Token);

        Assert.Equal(cts.Token, context.CommandInterceptor.LastReaderCancellationToken);
    }

    [Fact]
    public async Task GetSecurityAlertsAsync_propagates_caller_supplied_cancellation_token_to_the_database_call()
    {
        await using var context = await CreateContextAsync();
        var service = new SecurityAuditService(context.Db);
        using var cts = new CancellationTokenSource();

        await service.GetSecurityAlertsAsync(new SecurityAlertFilterRequest(), cts.Token);

        Assert.Equal(cts.Token, context.CommandInterceptor.LastReaderCancellationToken);
    }

    [Fact]
    public async Task MarkAllAlertsAsReadAsync_responds_to_an_already_cancelled_request()
    {
        await using var context = await CreateContextAsync();
        var service = new SecurityAuditService(context.Db);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.MarkAllAlertsAsReadAsync(cts.Token));
    }

    [Fact]
    public async Task MarkAlertAsReadAsync_responds_to_an_already_cancelled_request()
    {
        await using var context = await CreateContextAsync();
        var service = new SecurityAuditService(context.Db);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.MarkAlertAsReadAsync(1, cts.Token));
    }

    [Fact]
    public async Task RecordLoginAttemptAsync_persists_failed_attempt_even_when_the_request_is_already_aborted()
    {
        await using var context = await CreateContextAsync();
        var service = new SecurityAuditService(context.Db);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var httpContext = CreateHttpContext(cts.Token);

        await service.RecordLoginAttemptAsync("attacker", null, false, "invalid_credentials", httpContext);

        var log = await context.Db.LoginAttemptLogs.AsNoTracking().SingleAsync();
        Assert.Equal("attacker", log.Username);
        Assert.False(log.Succeeded);
        Assert.Equal("invalid_credentials", log.FailureReason);
        Assert.Equal("medium", log.RiskLevel);
        Assert.Equal("10.0.0.5", log.IpAddress);
        Assert.Equal("xunit-agent", log.UserAgent);
    }

    [Fact]
    public async Task RecordUnauthorizedAccessAsync_persists_forbidden_event_even_when_the_request_is_already_aborted()
    {
        await using var context = await CreateContextAsync();
        var service = new SecurityAuditService(context.Db);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var httpContext = CreateHttpContext(cts.Token);
        httpContext.Request.Path = "/api/reports/some-report";

        await service.RecordUnauthorizedAccessAsync(httpContext, 403, "forbidden");

        var log = await context.Db.LoginAttemptLogs.AsNoTracking().SingleAsync();
        Assert.False(log.Succeeded);
        Assert.Equal("forbidden_access", log.FailureReason);
        Assert.Equal("high", log.RiskLevel);
        Assert.Equal("10.0.0.5", log.IpAddress);
    }

    [Fact]
    public async Task EvaluateLoginRiskAsync_still_raises_a_critical_spray_alert_when_the_request_is_already_aborted()
    {
        await using var context = await CreateContextAsync();
        var service = new SecurityAuditService(context.Db);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var httpContext = CreateHttpContext(cts.Token);

        for (var i = 0; i < 5; i++)
        {
            await service.RecordLoginAttemptAsync($"user{i}", null, false, "invalid_credentials", httpContext);
        }

        var alert = await context.Db.SecurityAlerts.AsNoTracking()
            .SingleAsync(a => a.Type == "ip_password_spray");
        Assert.Equal("critical", alert.Severity);
        Assert.Equal("10.0.0.5", alert.IpAddress);
    }

    [Fact]
    public async Task RecordLoginAttemptAsync_does_not_swallow_a_genuine_database_failure()
    {
        await using var context = await CreateContextAsync();
        var service = new SecurityAuditService(context.Db);
        var httpContext = CreateHttpContext();
        context.SaveChangesInterceptor.ExceptionToThrow =
            new InvalidOperationException("simulated database failure");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordLoginAttemptAsync("someone", null, false, "invalid_credentials", httpContext));

        Assert.Equal("simulated database failure", exception.Message);
    }
}
