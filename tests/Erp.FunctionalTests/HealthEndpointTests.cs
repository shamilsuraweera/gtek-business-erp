using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Erp.Modules.Platform.Infrastructure.Persistence;
using Erp.Modules.Platform.Application;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Erp.FunctionalTests;

[Collection("Functional API")]
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_endpoint_is_available()
    {
        using var response = await _client.GetAsync("/api/v1/health");

        Assert.True(response.IsSuccessStatusCode);
    }

    [Collection("Functional API")]
    public sealed class CompanyEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public CompanyEndpointTests(WebApplicationFactory<Program> factory)
        {
            _client = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Authentication:BootstrapSecret", "test-bootstrap-secret");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<PlatformDbContext>>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<PlatformDbContext>();
                    services.RemoveAll<ICompanyStore>();
                    services.RemoveAll<IUserStore>();
                    services.RemoveAll<IRolePermissionStore>();
                    services.RemoveAll<IAuditEntryStore>();
                    services.AddDbContext<PlatformDbContext>(options =>
                        options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
                    services.AddSingleton<ICompanyStore, InMemoryCompanyStore>();
                    services.AddSingleton<IUserStore, InMemoryUserStore>();
                    services.AddSingleton<IRolePermissionStore, InMemoryUserStore.InMemoryRolePermissionStore>();
                    services.AddSingleton<IAuditEntryStore, InMemoryAuditEntryStore>();
                    services.AddSingleton<ICompanyAccessAuthorizer, AllowAllCompanyAccessAuthorizer>();
                });
            }).CreateClient();
        }

        [Fact]
        public async Task Company_can_be_created_and_retrieved()
        {
            await AuthenticateAsync();
            using var create = await _client.PostAsJsonAsync("/api/v1/companies", new { code = $"DEMO{Guid.NewGuid():N}"[..12], name = "Demo Company" });
            Assert.True(create.StatusCode == HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
            var response = await create.Content.ReadFromJsonAsync<CompanyResponse>();
            Assert.NotNull(response);

            using var get = await _client.GetAsync($"/api/v1/companies/{response!.Id}");
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        }

        [Fact]
        public async Task Invalid_company_returns_problem_details()
        {
            await AuthenticateAsync();
            using var response = await _client.PostAsJsonAsync("/api/v1/companies", new { code = "", name = "Demo" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        private async Task AuthenticateAsync()
        {
            _client.DefaultRequestHeaders.Remove("Authorization");
            var username = $"test{Guid.NewGuid():N}";
            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users")
            {
                Content = JsonContent.Create(new
                {
                    userName = username,
                    email = $"{username}@example.com",
                    password = "CorrectHorseBattery12!"
                })
            };
            createRequest.Headers.Add("X-Bootstrap-Secret", "test-bootstrap-secret");
            using var create = await _client.SendAsync(createRequest);
            Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
            create.Dispose();
            using var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { userName = username, password = "CorrectHorseBattery12!" });
            login.EnsureSuccessStatusCode();
            var result = await login.Content.ReadFromJsonAsync<JsonElement>();
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", result.GetProperty("accessToken").GetString());
        }

        [Fact]
        public async Task Company_scoped_endpoint_requires_active_company_header()
        {
            await AuthenticateAsync();
            using var response = await _client.GetAsync("/api/v1/finance/accounts");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Login_and_me_return_safe_identity()
        {
            await AuthenticateAsync();
            using var response = await _client.GetAsync("/api/v1/auth/me");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hash", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Protected_company_management_requires_authentication()
        {
            using var response = await _client.GetAsync("/api/v1/companies");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Number_sequence_management_requires_authentication()
        {
            using var response = await _client.PostAsJsonAsync(
                "/api/v1/number-sequences",
                new
                {
                    code = "TEST",
                    name = "Test sequence",
                    prefix = "T-",
                    startingValue = 1,
                    padding = 4,
                    increment = 1
                });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Audit_query_requires_authentication()
        {
            using var response = await _client.GetAsync("/api/v1/audit");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Bootstrap_administrator_can_query_audit_history_without_company_context()
        {
            await AuthenticateAsync();
            using var response = await _client.GetAsync("/api/v1/audit?page=1&pageSize=10");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("jwt", body, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class InMemoryCompanyStore : ICompanyStore
        {
            private readonly List<Company> _companies = [];

            public Task AddAsync(Company company, CancellationToken cancellationToken)
            {
                _companies.Add(company);
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<Company>> ListAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<Company>>(_companies.OrderBy(company => company.Code).ToArray());

            public Task<Company?> GetByIdAsync(CompanyId id, CancellationToken cancellationToken) =>
                Task.FromResult(_companies.SingleOrDefault(company => company.Id == id));

            public Task<Company?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
                Task.FromResult(_companies.SingleOrDefault(company => company.Code == code));

            public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken) =>
                Task.FromResult(_companies.Any(company => company.Code == code));

            public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class AllowAllCompanyAccessAuthorizer : ICompanyAccessAuthorizer
        {
            public Task<bool> CanAccessCompanyAsync(UserId userId, CompanyId companyId, CancellationToken cancellationToken) =>
                Task.FromResult(true);
        }

        private sealed class InMemoryAuditEntryStore : IAuditEntryStore
        {
            private readonly List<AuditEntry> entries = [];

            public Task AddAsync(AuditEntry entry, CancellationToken cancellationToken)
            {
                entries.Add(entry);
                return Task.CompletedTask;
            }

            public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<(IReadOnlyList<AuditEntry> Items, int TotalCount)> QueryAsync(
                AuditQuery query,
                CancellationToken cancellationToken)
            {
                var filtered = entries.AsEnumerable();
                if (query.Category is not null) filtered = filtered.Where(x => x.Category == query.Category);
                if (query.Action is not null) filtered = filtered.Where(x => x.Action == query.Action);
                var ordered = filtered.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).ToArray();
                return Task.FromResult(((IReadOnlyList<AuditEntry>)ordered
                    .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray(), ordered.Length));
            }
        }

        private sealed class InMemoryUserStore : IUserStore
        {
            private readonly List<User> users = [];
            private readonly Dictionary<UserId, string> hashes = [];

            public Task AddAsync(User user, string passwordHash, CancellationToken cancellationToken)
            {
                users.Add(user);
                hashes[user.Id] = passwordHash;
                return Task.CompletedTask;
            }

            public sealed class InMemoryRolePermissionStore : IRolePermissionStore
            {
                private readonly List<Role> roles = [];
                private readonly List<Permission> permissions = [];
                private readonly HashSet<(UserId User, RoleId Role)> userRoles = [];
                private readonly HashSet<(RoleId Role, PermissionId Permission)> rolePermissions = [];

                public Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Role>>(roles.ToArray());
                public Task<Role?> GetRoleAsync(RoleId id, CancellationToken ct) => Task.FromResult(roles.SingleOrDefault(x => x.Id == id));
                public Task<Role?> GetRoleByCodeAsync(string code, CancellationToken ct) => Task.FromResult(roles.SingleOrDefault(x => x.Code == code));
                public Task<bool> RoleCodeExistsAsync(string code, CancellationToken ct) => Task.FromResult(roles.Any(x => x.Code == code));
                public Task AddRoleAsync(Role role, CancellationToken ct) { roles.Add(role); return Task.CompletedTask; }
                public Task<IReadOnlyList<Permission>> ListPermissionsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Permission>>(permissions.ToArray());
                public Task<Permission?> GetPermissionByCodeAsync(string code, CancellationToken ct) => Task.FromResult(permissions.SingleOrDefault(x => x.Code == code));
                public Task<IReadOnlyList<Permission>> ListRolePermissionsAsync(RoleId roleId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Permission>>(permissions.Where(p => rolePermissions.Contains((roleId, p.Id))).ToArray());
                public Task<bool> HasRolePermissionAsync(RoleId roleId, PermissionId permissionId, CancellationToken ct) => Task.FromResult(rolePermissions.Contains((roleId, permissionId)));
                public Task AddRolePermissionAsync(RoleId roleId, PermissionId permissionId, CancellationToken ct) { rolePermissions.Add((roleId, permissionId)); return Task.CompletedTask; }
                public Task RemoveRolePermissionAsync(RoleId roleId, PermissionId permissionId, CancellationToken ct) { rolePermissions.Remove((roleId, permissionId)); return Task.CompletedTask; }
                public Task<IReadOnlyList<Role>> ListUserRolesAsync(UserId userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Role>>(roles.Where(r => userRoles.Contains((userId, r.Id))).ToArray());
                public Task<bool> HasUserRoleAsync(UserId userId, RoleId roleId, CancellationToken ct) => Task.FromResult(userRoles.Contains((userId, roleId)));
                public Task AddUserRoleAsync(UserId userId, RoleId roleId, CancellationToken ct) { userRoles.Add((userId, roleId)); return Task.CompletedTask; }
                public Task RemoveUserRoleAsync(UserId userId, RoleId roleId, CancellationToken ct) { userRoles.Remove((userId, roleId)); return Task.CompletedTask; }
                public Task<bool> UserHasPermissionAsync(UserId userId, string code, CancellationToken ct) =>
                    Task.FromResult(ListUserRolesAsync(userId, ct).Result.Any(r => r.Status == RoleStatus.Active && permissions.Where(p => p.Code == code).Any(p => rolePermissions.Contains((r.Id, p.Id)))));
                public Task EnsurePermissionCatalogueAsync(CancellationToken ct)
                {
                    foreach (var item in Permissions.Catalogue.Where(item => permissions.All(p => p.Code != item.Code)))
                        permissions.Add(Permission.Create(item.Code, item.Name, item.Module, item.Description, DateTimeOffset.UtcNow));
                    return Task.CompletedTask;
                }
                public async Task EnsureSystemAdministratorAsync(UserId userId, CancellationToken ct)
                {
                    var role = roles.SingleOrDefault(r => r.Code == "SYSTEM_ADMIN") ?? Role.Create("SYSTEM_ADMIN", "System Administrator", null, true, DateTimeOffset.UtcNow);
                    if (!roles.Contains(role)) roles.Add(role);
                    foreach (var permission in permissions) rolePermissions.Add((role.Id, permission.Id));
                    userRoles.Add((userId, role.Id));
                    await Task.CompletedTask;
                }
                public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
            }

            public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<User>>(users.OrderBy(user => user.UserName).ToArray());

            public Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken) =>
                Task.FromResult(users.SingleOrDefault(user => user.Id == id));

            public Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken) =>
                Task.FromResult(users.SingleOrDefault(user => user.UserName == userName));

            public Task<bool> ExistsByUserNameAsync(string userName, CancellationToken cancellationToken) =>
                Task.FromResult(users.Any(user => user.UserName == userName));

            public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken) =>
                Task.FromResult(users.Any(user => user.Email == email));

            public Task<string?> GetPasswordHashAsync(UserId id, CancellationToken cancellationToken) =>
                Task.FromResult(hashes.TryGetValue(id, out var hash) ? hash : null);

            public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<bool> HasUsersAsync(CancellationToken cancellationToken) =>
                Task.FromResult(users.Count != 0);
        }
    }

    [CollectionDefinition("Functional API", DisableParallelization = true)]
    public sealed class FunctionalApiCollection;
}
