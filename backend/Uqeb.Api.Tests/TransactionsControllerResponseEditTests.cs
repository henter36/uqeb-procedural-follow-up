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

public class TransactionsControllerResponseEditTests
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
        public Func<int, CompleteResponseRequest, ICurrentUserService, Task<TransactionDetailDto?>>? OnEditResponse { get; set; }

        public Task<TransactionDetailDto?> EditResponseAsync(int id, CompleteResponseRequest request, ICurrentUserService currentUser) =>
            OnEditResponse?.Invoke(id, request, currentUser) ?? throw new NotImplementedException();

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
        public Task<bool> CloseAsync(int id, int userId, UserRole role) => throw new NotImplementedException();
        public Task<TransactionDetailDto?> CompleteResponseAsync(int id, CompleteResponseRequest request, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<List<FollowUpDepartmentOptionDto>?> GetFollowUpDepartmentsAsync(int transactionId, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<FollowUpDto> AddFollowUpAsync(int transactionId, CreateFollowUpRequest request, int userId) => throw new NotImplementedException();
        public Task<FollowUpDto?> ReplyFollowUpAsync(int transactionId, int followUpId, ReplyFollowUpRequest request, int userId) => throw new NotImplementedException();
        public Task<AssignmentDto> AddAssignmentAsync(int transactionId, CreateAssignmentRequest request, int userId) => throw new NotImplementedException();
        public Task<AssignmentDto?> ReplyAssignmentAsync(int transactionId, int assignmentId, ReplyAssignmentRequest request, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<AssignmentDto?> AdminEditAssignmentAsync(int transactionId, int assignmentId, AdminEditAssignmentRequest request, int userId) => throw new NotImplementedException();
        public Task<TransactionDetailDto?> AdminEditTransactionDatesAsync(int transactionId, AdminEditTransactionDatesRequest request, int userId) => throw new NotImplementedException();
        public Task<PagedResult<AuditLogDto>> GetAuditLogAsync(int transactionId, int page, int pageSize, ICurrentUserService currentUser) => throw new NotImplementedException();
        public Task<TransactionDetailDto?> EnableRecurringAsync(int id, EnableRecurringForTransactionRequest request, int userId) => throw new NotImplementedException();
        public Task<bool> CanAccessTransactionAsync(int transactionId, ICurrentUserService currentUser) => throw new NotImplementedException();
    }

    private static TransactionsController CreateController(FakeTransactionService service) =>
        new(service, null!, null!, null!, new StubCurrentUser());

    [Fact]
    public async Task EditResponse_returns_403_json_body_with_message_when_unauthorized()
    {
        var service = new FakeTransactionService
        {
            OnEditResponse = (_, _, _) => throw new UnauthorizedAccessException("لا تملك صلاحية تعديل الإفادة"),
        };
        var controller = CreateController(service);

        var result = await controller.EditResponse(1, new CompleteResponseRequest
        {
            ResponseDate = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc),
            ResponseSummary = "محاولة",
        });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        var value = Assert.IsType<string>(objectResult.Value!.GetType().GetProperty("message")!.GetValue(objectResult.Value));
        Assert.Equal("لا تملك صلاحية تعديل الإفادة", value);
    }
}
