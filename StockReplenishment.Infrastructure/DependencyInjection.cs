using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockReplenishment.Application;
using StockReplenishment.Infrastructure.Data;

namespace StockReplenishment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddStockInfrastructure(this IServiceCollection services, string databaseName)
    {
        services.AddDbContext<StockDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IRequestRepository, EfRequestRepository>();
        services.AddSingleton<IStockAvailability, SimulatedStockAvailability>();
        services.AddHostedService<StockValidationWorker>();
        return services;
    }

    public static async Task SeedStockDataAsync(this IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        await SeedData.InitializeAsync(scope.ServiceProvider.GetRequiredService<StockDbContext>());
    }
}
