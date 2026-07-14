using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Uqeb.Api.Controllers;
using Uqeb.Api.DTOs.Common;
using Uqeb.Api.DTOs.RecurringTemplates;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class TransactionsControllerReplyAssignmentTests
{
    private sealed class StubCurrentUser : ICurrentUserService
    {
        public int UserId => 1;
        public string Username => "admin";
        public UserRole Role => UserRole.Admin;
        public int? DepartmentId => null;
        public bool IsAuthenticated => true;
    }

    private sealed class FakeTransactionService : ITransactionService
    {
        public Func<int, int, ReplyAssignmentRequest, ICurrentUserService, Task<AssignmentDto?>>? OnReplyAssignment { get; set; }

        public Task<AssignmentDto?> ReplyAssignmentAsync(int transactionId, int assignmentId, ReplyAssignmentRequest request, ICurrentUserService currentUser) =>
            OnReplyAssignment?.Invoke(transactionId, assignmentId, request, currentUser) ?? throw new NotImplementedException();

        public Task<TransactionDetailDto?> EditResponseAsync(int id, CompleteResponseRequest request, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<PagedResult<TransactionListDto>> SearchAsync(TransactionSearchRequest request, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<TransactionDetailDto?> GetByIdAsync(int id, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<TransactionDetailDto?> GetBasicByIdAsync(int id, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<TransactionWorkspaceDto?> GetWorkspaceAsync(int id, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<List<AssignmentDto>?> GetAssignmentsAsync(int transactionId, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<List<FollowUpDto>?> GetFollowUpsAsync(int transactionId, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<TransactionDetailDto> CreateAsync(CreateTransactionRequest request, int userId) => throw new NotImplementedException();
        public Task<TransactionDetailDto?> UpdateAsync(int id, UpdateTransactionRequest request, int userId, UserRole role) => throw new NotImplementedException();
        public Task<bool> CancelAsync(int id, int userId, UserRole role) => throw new NotImplementedException();
        public Task<bool> ArchiveAsync(int id, int userId, UserRole role) => throw new NotImplementedException();
        public Task<bool> CloseAsync(int id, CloseTransactionRequest request, int userId, UserRole role) => throw new NotImplementedException();
        public Task<TransactionDetailDto?> CompleteResponseAsync(int id, CompleteResponseRequest request, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<List<FollowUpDepartmentOptionDto>?> GetFollowUpDepartmentsAsync(int transactionId, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<FollowUpDto> AddFollowUpAsync(int transactionId, CreateFollowUpRequest request, int userId) => throw new NotImplementedException();
        public Task<FollowUpDto?> ReplyFollowUpAsync(int transactionId, int followUpId, ReplyFollowUpRequest request, int userId) => throw new NotImplementedException();
        public Task<FollowUpDto?> EditFollowUpReplyAsync(int transactionId, int followUpId, ReplyFollowUpRequest request, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<AssignmentDto?> EditAssignmentReplyAsync(int transactionId, int assignmentId, ReplyAssignmentRequest request, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<AssignmentDto> AddAssignmentAsync(int transactionId, CreateAssignmentRequest request, int userId) => throw new NotImplementedException();
        public Task<AssignmentDto?> AdminEditAssignmentAsync(int transactionId, int assignmentId, AdminEditAssignmentRequest request, int userId) => throw new NotImplementedException();
        public Task<TransactionDetailDto?> AdminEditTransactionDatesAsync(int transactionId, AdminEditTransactionDatesRequest request, int userId) => throw new NotImplementedException();
        public Task<PagedResult<AuditLogDto>> GetAuditLogAsync(int transactionId, int page, int pageSize, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<TransactionDetailDto?> EnableRecurringAsync(int id, EnableRecurringForTransactionRequest request, int userId) => throw new NotImplementedException();
        public Task<bool> CanAccessTransactionAsync(int transactionId, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<TransactionAdjacentDto?> GetAdjacentAsync(int id, ICurrentUserService currentUser) => throw new NotImplementedException();
    }

    private static TransactionsController CreateController(FakeTransactionService service) =>
        new(service, null!, null!, null!, new StubCurrentUser());

    // Regression test for the real bug: ReplyAssignmentAsync's own date-order checks throw
    // InvalidOperationException ("لا يمكن أن يسبق تاريخ الوارد/الإحالة"), but the POST
    // .../assignments/{id}/reply action only caught UnauthorizedAccessException and
    // FieldValidationException, so ASP.NET's default handler turned this into a 500 instead
    // of a 400 — unlike the sibling PATCH edit endpoint, which already caught it correctly.
    [Fact]
    public async Task ReplyAssignment_returns_400_json_body_when_reply_date_precedes_assigned_date()
    {
        var service = new FakeTransactionService
        {
            OnReplyAssignment = (_, _, _, _) => throw new InvalidOperationException("تاريخ إنجاز الإدارة لا يمكن أن يسبق تاريخ الإحالة."),
        };
        var controller = CreateController(service);

        var result = await controller.ReplyAssignment(1, 501, new ReplyAssignmentRequest
        {
            ReplyDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ReplySummary = "رد",
        });

        var objectResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var value = Assert.IsType<string>(objectResult.Value!.GetType().GetProperty("message")!.GetValue(objectResult.Value));
        Assert.Equal("تاريخ إنجاز الإدارة لا يمكن أن يسبق تاريخ الإحالة.", value);
    }

    [Fact]
    public async Task ReplyAssignment_returns_400_json_body_when_reply_date_precedes_incoming_date()
    {
        var service = new FakeTransactionService
        {
            OnReplyAssignment = (_, _, _, _) => throw new InvalidOperationException("تاريخ إنجاز الإدارة لا يمكن أن يسبق تاريخ الوارد."),
        };
        var controller = CreateController(service);

        var result = await controller.ReplyAssignment(1, 501, new ReplyAssignmentRequest
        {
            ReplyDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ReplySummary = "رد",
        });

        var objectResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var value = Assert.IsType<string>(objectResult.Value!.GetType().GetProperty("message")!.GetValue(objectResult.Value));
        Assert.Equal("تاريخ إنجاز الإدارة لا يمكن أن يسبق تاريخ الوارد.", value);
    }
}
