using Service;

namespace api.Accrual;

/// <summary>
/// Runs the holiday accrual when the app starts and then once a day. Because the accrual catches up on
/// any month it missed, it does not matter if the app was switched off for a while.
/// </summary>
public class AccrualBackgroundService(IServiceScopeFactory scopes, ILogger<AccrualBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));

        do
        {
            try
            {
                // A background job has no web request, so it makes its own scope (and its own database connection)
                using var scope = scopes.CreateScope();
                var accrual = scope.ServiceProvider.GetRequiredService<AccrualService>();

                var result = await accrual.RunAsync(DateOnly.FromDateTime(DateTime.Today));
                logger.LogInformation(
                    "Holiday accrual: {Employees} employees checked, {Buckets} buckets created, {Entries} ledger entries booked.",
                    result.EmployeesProcessed, result.BucketsCreated, result.EntriesCreated);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Holiday accrual failed. It will be tried again at the next run.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}