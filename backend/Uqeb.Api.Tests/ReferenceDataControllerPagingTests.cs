using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Uqeb.Api.Controllers;
using Uqeb.Api.DTOs.Categories;
using Uqeb.Api.DTOs.Common;
using Uqeb.Api.DTOs.Departments;
using Uqeb.Api.DTOs.ExternalParties;
using Uqeb.Api.DTOs.Reports;
using Uqeb.Api.DTOs.Transactions;
using Uqeb.Api.DTOs.Users;
using Uqeb.Api.Models.Enums;
using Uqeb.Api.Services;
using Xunit;

namespace Uqeb.Api.Tests;

public class ReferenceDataControllerPagingTests
{
    private sealed class TestCacheInvalidation : ICacheInvalidationService
    {
        public string DashboardSummaryKey => "dashboard";
        public TimeSpan DashboardCacheDuration => TimeSpan.FromMinutes(1);
        public TimeSpan ReportsPageSummaryCacheDuration => TimeSpan.FromMinutes(1);
        public TimeSpan ReferenceDataCacheDuration => TimeSpan.FromMinutes(1);
        public string BuildReportsPageSummaryKey(ReportFilterRequest? filter) => "reports";
        public string BuildDepartmentsKey(bool activeOnly) => $"departments-{activeOnly}";
        public string BuildCategoriesKey(bool activeOnly) => $"categories-{activeOnly}";
        public string BuildExternalPartiesKey(bool activeOnly) => $"parties-{activeOnly}";
        public void InvalidateOnTransactionChange() { }
        public void InvalidateReferenceData() { }
    }

    private sealed class StubCurrentUser : ICurrentUserService
    {
        public int UserId => 1;
        public string Username => "admin";
        public UserRole Role => UserRole.Admin;
        public int? DepartmentId => null;
        public bool IsAuthenticated => true;
    }

    private sealed class TrackingDepartmentService : IDepartmentService
    {
        public bool GetAllInvoked { get; private set; }
        public bool SearchInvoked { get; private set; }
        public ReferenceDataListRequest? LastSearchRequest { get; private set; }

        public Task<List<DepartmentDto>> GetAllAsync(bool activeOnly = true)
        {
            GetAllInvoked = true;
            return Task.FromResult(new List<DepartmentDto> { new() { Id = 1, Name = "Finance", IsActive = true } });
        }

        public Task<PagedResult<DepartmentDto>> SearchAsync(ReferenceDataListRequest request, CancellationToken cancellationToken = default)
        {
            SearchInvoked = true;
            LastSearchRequest = request;
            var page = request.Page ?? 1;
            var pageSize = request.PageSize ?? 20;
            return Task.FromResult(PagedResult<DepartmentDto>.Create(
                [new DepartmentDto { Id = 1, Name = "Finance", IsActive = true }],
                1,
                page,
                pageSize));
        }

        public Task<List<LookupItemDto>> LookupAsync(LookupRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<DepartmentDto?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<DepartmentDto> CreateAsync(CreateDepartmentRequest request, int actorUserId) => throw new NotImplementedException();
        public Task<DepartmentDto?> UpdateAsync(int id, UpdateDepartmentRequest request, int actorUserId) => throw new NotImplementedException();
    }

    private sealed class TrackingExternalPartyService : IExternalPartyService
    {
        public bool GetAllInvoked { get; private set; }
        public bool SearchInvoked { get; private set; }

        public Task<List<ExternalPartyDto>> GetAllAsync(bool activeOnly = true)
        {
            GetAllInvoked = true;
            return Task.FromResult(new List<ExternalPartyDto> { new() { Id = 1, Name = "Party", IsActive = true } });
        }

        public Task<PagedResult<ExternalPartyDto>> SearchAsync(ReferenceDataListRequest request, CancellationToken cancellationToken = default)
        {
            SearchInvoked = true;
            return Task.FromResult(PagedResult<ExternalPartyDto>.Create([], 0, request.Page ?? 1, request.PageSize ?? 20));
        }

        public Task<List<LookupItemDto>> LookupAsync(LookupRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ExternalPartyDto?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<ExternalPartyDto> CreateAsync(CreateExternalPartyRequest request, int actorUserId) => throw new NotImplementedException();
        public Task<ExternalPartyDto?> UpdateAsync(int id, UpdateExternalPartyRequest request, int actorUserId) => throw new NotImplementedException();
    }

    private sealed class TrackingCategoryService : ICategoryService
    {
        public bool GetAllInvoked { get; private set; }
        public bool SearchInvoked { get; private set; }

        public Task<List<CategoryDto>> GetAllAsync(bool activeOnly = true)
        {
            GetAllInvoked = true;
            return Task.FromResult(new List<CategoryDto> { new() { Id = 1, Name = "General", IsActive = true } });
        }

        public Task<PagedResult<CategoryDto>> SearchAsync(ReferenceDataListRequest request, CancellationToken cancellationToken = default)
        {
            SearchInvoked = true;
            return Task.FromResult(PagedResult<CategoryDto>.Create([], 0, request.Page ?? 1, request.PageSize ?? 20));
        }

        public Task<List<LookupItemDto>> LookupAsync(LookupRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<CategoryDto?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<CategoryDto> CreateAsync(CreateCategoryRequest request, int actorUserId) => throw new NotImplementedException();
        public Task<CategoryDto?> UpdateAsync(int id, UpdateCategoryRequest request, int actorUserId) => throw new NotImplementedException();
    }

    private sealed class TrackingUserService : IUserService
    {
        public bool GetAllInvoked { get; private set; }
        public bool SearchInvoked { get; private set; }

        public Task<List<UserDto>> GetAllAsync()
        {
            GetAllInvoked = true;
            return Task.FromResult(new List<UserDto> { new() { Id = 1, Username = "admin", FullName = "Admin", Role = "Admin", IsActive = true } });
        }

        public Task<PagedResult<UserDto>> SearchAsync(ReferenceDataListRequest request, CancellationToken cancellationToken = default)
        {
            SearchInvoked = true;
            return Task.FromResult(PagedResult<UserDto>.Create([], 0, request.Page ?? 1, request.PageSize ?? 20));
        }

        public Task<UserDto?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<UserDto> CreateAsync(CreateUserRequest request, int actorUserId) => throw new NotImplementedException();
        public Task<UserDto?> UpdateAsync(int id, UpdateUserRequest request, int actorUserId) => throw new NotImplementedException();
        public Task<bool> ResetPasswordAsync(int id, ResetPasswordRequest request, int actorUserId) => throw new NotImplementedException();
    }

    private static ControllerContext QueryContext(string? queryString)
    {
        var http = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(queryString))
        {
            var value = queryString.StartsWith('?') ? queryString : "?" + queryString;
            http.Request.QueryString = new QueryString(value);
        }

        return new ControllerContext { HttpContext = http };
    }

    [Theory]
    [InlineData(typeof(DepartmentsController))]
    [InlineData(typeof(ExternalPartiesController))]
    [InlineData(typeof(CategoriesController))]
    public async Task GetAll_WithoutPage_ReturnsFlatList(Type controllerType)
    {
        if (controllerType == typeof(DepartmentsController))
        {
            var service = new TrackingDepartmentService();
            var controller = new DepartmentsController(service, new MemoryCache(new MemoryCacheOptions()), new TestCacheInvalidation(), new StubCurrentUser())
            {
                ControllerContext = QueryContext(null)
            };
            var result = await controller.GetAll();
            Assert.True(service.GetAllInvoked);
            Assert.False(service.SearchInvoked);
            Assert.IsAssignableFrom<List<DepartmentDto>>(Assert.IsType<OkObjectResult>(result).Value);
            return;
        }

        if (controllerType == typeof(ExternalPartiesController))
        {
            var service = new TrackingExternalPartyService();
            var controller = new ExternalPartiesController(service, new MemoryCache(new MemoryCacheOptions()), new TestCacheInvalidation(), new StubCurrentUser())
            {
                ControllerContext = QueryContext(null)
            };
            var result = await controller.GetAll();
            Assert.True(service.GetAllInvoked);
            Assert.False(service.SearchInvoked);
            Assert.IsAssignableFrom<List<ExternalPartyDto>>(Assert.IsType<OkObjectResult>(result).Value);
            return;
        }

        var categoryService = new TrackingCategoryService();
        var categoriesController = new CategoriesController(categoryService, new MemoryCache(new MemoryCacheOptions()), new TestCacheInvalidation(), new StubCurrentUser())
        {
            ControllerContext = QueryContext(null)
        };
        var categoryResult = await categoriesController.GetAll();
        Assert.True(categoryService.GetAllInvoked);
        Assert.False(categoryService.SearchInvoked);
        Assert.IsAssignableFrom<List<CategoryDto>>(Assert.IsType<OkObjectResult>(categoryResult).Value);
    }

    [Fact]
    public async Task Users_GetWithoutPage_ReturnsFlatList()
    {
        var service = new TrackingUserService();
        var controller = new UsersController(service, new StubCurrentUser())
        {
            ControllerContext = QueryContext(null)
        };

        var result = await controller.GetAll(null, CancellationToken.None);

        Assert.True(service.GetAllInvoked);
        Assert.False(service.SearchInvoked);
        Assert.IsAssignableFrom<List<UserDto>>(Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task Departments_GetWithActiveOnlyOnly_ReturnsFlatList()
    {
        var service = new TrackingDepartmentService();
        var controller = new DepartmentsController(service, new MemoryCache(new MemoryCacheOptions()), new TestCacheInvalidation(), new StubCurrentUser())
        {
            ControllerContext = QueryContext("?activeOnly=false")
        };

        var result = await controller.GetAll(activeOnly: false);

        Assert.True(service.GetAllInvoked);
        Assert.False(service.SearchInvoked);
        Assert.IsAssignableFrom<List<DepartmentDto>>(Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task Departments_GetWithPage_ReturnsPagedResult()
    {
        var service = new TrackingDepartmentService();
        var controller = new DepartmentsController(service, new MemoryCache(new MemoryCacheOptions()), new TestCacheInvalidation(), new StubCurrentUser())
        {
            ControllerContext = QueryContext("?page=1&pageSize=20")
        };

        var result = await controller.GetAll(activeOnly: true, list: new ReferenceDataListRequest());

        Assert.False(service.GetAllInvoked);
        Assert.True(service.SearchInvoked);
        Assert.IsAssignableFrom<PagedResult<DepartmentDto>>(Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task Departments_GetWithSearchStatusAndPage_UsesSearch()
    {
        var service = new TrackingDepartmentService();
        var controller = new DepartmentsController(service, new MemoryCache(new MemoryCacheOptions()), new TestCacheInvalidation(), new StubCurrentUser())
        {
            ControllerContext = QueryContext("?page=1&pageSize=20&search=fin&status=active")
        };

        await controller.GetAll(list: new ReferenceDataListRequest { Search = "fin", Status = "active" });

        Assert.True(service.SearchInvoked);
        Assert.Equal("fin", service.LastSearchRequest?.Search);
        Assert.Equal("active", service.LastSearchRequest?.Status);
    }

    [Fact]
    public async Task Users_GetWithPage_ReturnsPagedResult()
    {
        var service = new TrackingUserService();
        var controller = new UsersController(service, new StubCurrentUser())
        {
            ControllerContext = QueryContext("?page=1&pageSize=20")
        };

        var result = await controller.GetAll(new ReferenceDataListRequest(), CancellationToken.None);

        Assert.False(service.GetAllInvoked);
        Assert.True(service.SearchInvoked);
        Assert.IsAssignableFrom<PagedResult<UserDto>>(Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task ExternalParties_GetWithPage_ReturnsPagedResult()
    {
        var service = new TrackingExternalPartyService();
        var controller = new ExternalPartiesController(service, new MemoryCache(new MemoryCacheOptions()), new TestCacheInvalidation(), new StubCurrentUser())
        {
            ControllerContext = QueryContext("?page=1&pageSize=20")
        };

        var result = await controller.GetAll(list: new ReferenceDataListRequest());

        Assert.False(service.GetAllInvoked);
        Assert.True(service.SearchInvoked);
        Assert.IsAssignableFrom<PagedResult<ExternalPartyDto>>(Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task Categories_GetWithPage_ReturnsPagedResult()
    {
        var service = new TrackingCategoryService();
        var controller = new CategoriesController(service, new MemoryCache(new MemoryCacheOptions()), new TestCacheInvalidation(), new StubCurrentUser())
        {
            ControllerContext = QueryContext("?page=1&pageSize=20")
        };

        var result = await controller.GetAll(list: new ReferenceDataListRequest());

        Assert.False(service.GetAllInvoked);
        Assert.True(service.SearchInvoked);
        Assert.IsAssignableFrom<PagedResult<CategoryDto>>(Assert.IsType<OkObjectResult>(result).Value);
    }
}

public class ReferenceDataQueryHelperTests
{
    [Fact]
    public void NormalizeListRequest_AppliesDefaultsAndCapsPageSize()
    {
        var normalized = ReferenceDataQueryHelper.NormalizeListRequest(new ReferenceDataListRequest
        {
            PageSize = 500,
            SortDesc = null,
            Page = null,
        });

        Assert.Equal(1, normalized.Page);
        Assert.Equal(100, normalized.PageSize);
        Assert.False(normalized.SortDesc);
        Assert.Equal("name", normalized.SortBy);
    }

    [Fact]
    public void NormalizeLookupRequest_DefaultsActiveOnlyAndLimit()
    {
        var normalized = ReferenceDataQueryHelper.NormalizeLookupRequest(new LookupRequest());

        Assert.True(normalized.ActiveOnly);
        Assert.Equal(50, normalized.Limit);
    }
}
