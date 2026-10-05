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

namespace Erp.FunctionalTests;

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

    public sealed class CompanyEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public CompanyEndpointTests(WebApplicationFactory<Program> factory)
        {
            _client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<PlatformDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<PlatformDbContext>();
                services.RemoveAll<ICompanyStore>();
                services.AddDbContext<PlatformDbContext>(options =>
                    options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
                services.AddSingleton<ICompanyStore, InMemoryCompanyStore>();
            })).CreateClient();
        }

        [Fact]
        public async Task Company_can_be_created_and_retrieved()
        {
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
            using var response = await _client.PostAsJsonAsync("/api/v1/companies", new { code = "", name = "Demo" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public async Task Company_scoped_endpoint_requires_active_company_header()
        {
            using var response = await _client.GetAsync("/api/v1/finance/accounts");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
    }
}
