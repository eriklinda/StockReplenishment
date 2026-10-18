using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StockReplenishment.Application;
using StockReplenishment.Contracts;

namespace StockReplenishment.Infrastructure;

/// <summary>
/// Picks up requests with a pending stock check and calls the slow external service in the background,
/// so the submit endpoint can return immediately. Clients poll the request to see the result.
/// </summary>
public class StockValidationWorker(
    IServiceScopeFactory scopeFactory,
    IStockAvailability stockAvailability,
    ILogger<StockValidationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ExternalCallTimeout = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = await GetPendingValidations(stoppingToken);
                await Task.WhenAll(pending.Select(request => Validate(request, stoppingToken)));
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Stock validation worker iteration failed");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task<List<RequestResponse>> GetPendingValidations(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RequestService>();
        return await service.GetPendingValidations(ct);
    }

    private async Task Validate(RequestResponse request, CancellationToken stoppingToken)
    {
        IReadOnlyDictionary<int, decimal>? availableQuantities = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(ExternalCallTimeout);
            availableQuantities = await stockAvailability.CheckAsync(request, timeout.Token);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // A null result marks the check as failed, so the user can retry it.
            logger.LogWarning(ex, "External stock check failed for request {RequestId}", request.Id);
        }

        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RequestService>();
        await service.CompleteValidation(request.Id, availableQuantities, stoppingToken);
    }
}
