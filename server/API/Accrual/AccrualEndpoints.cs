using api.Auth;
using Service;

namespace api.Accrual;

public static class AccrualEndpoints
{
    public static void MapAccrualEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accrual")
            .WithTags("Accrual")
            .AddEndpointFilter<ProblemFilter>();

        // POST /api/accrual/run?asOf=2026-11-01   (system owner only). Normally the daily job does this.
        group.MapPost("/run", async (DateOnly? asOf, AccrualService accrual) =>
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                var date = asOf ?? today;

                // Ledger rows can never be deleted, so we never book holiday that has not been earned yet
                if (date > today)
                    throw new RuleViolationException("asOf cannot be in the future.");

                return Results.Ok(await accrual.RunAsync(date));
            })
            .RequireAuthorization(Policies.SystemOwner);
    }
}