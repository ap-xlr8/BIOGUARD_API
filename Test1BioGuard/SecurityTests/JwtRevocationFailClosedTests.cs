using System.Net;
using System.Net.Http.Headers;
using BioGuard.Api.Models;
using MongoDB.Driver;
using Moq;
using Test1BioGuard.IntegrationTests;

namespace Test1BioGuard.SecurityTests;

public class JwtRevocationFailClosedTests
{
    [Fact]
    public async Task ProtectedEndpoint_RevocationStoreUnavailable_RejectsToken()
    {
        using var factory = new CustomWebApplicationFactory();
        factory.MockDbContext
            .Setup(db => db.FindFirstOrDefaultAsync(
                It.IsAny<IMongoCollection<TokenBlacklist>>(),
                It.IsAny<System.Linq.Expressions.Expression<Func<TokenBlacklist, bool>>>()))
            .ThrowsAsync(new TimeoutException("revocation store unavailable"));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestTokenHelper.GenerateDuenoToken());

        var response = await client.GetAsync("/api/UsuariosWeb/mi-perfil");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
