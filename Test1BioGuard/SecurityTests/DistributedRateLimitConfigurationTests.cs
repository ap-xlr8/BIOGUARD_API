using BioGuard.Api.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Test1BioGuard.SecurityTests;

public class DistributedRateLimitConfigurationTests
{
    [Fact]
    public void ProductionWithoutRedis_RefusesToStart()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.ConfigureRateLimiting(config, isProduction: true));

        Assert.Contains("REDIS_CONNECTION_STRING", exception.Message);
    }

    [Fact]
    public void NonProductionWithoutRedis_UsesSafeLocalFallback()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        services.ConfigureRateLimiting(config, isProduction: false);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType.FullName == "Microsoft.Extensions.Caching.Distributed.IDistributedCache");
    }
}
