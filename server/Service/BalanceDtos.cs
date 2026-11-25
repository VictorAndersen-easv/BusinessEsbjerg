namespace Service;

/// <summary>One earning period of one account, with what is left of it.</summary>
public record BucketBalanceDto(
    int BucketId,
    DateOnly EarningStart,
    DateOnly EarningEnd,
    DateOnly UsageStart,
    DateOnly UsageEnd,
    decimal EarnedHours,
    decimal EarnedDays,
    decimal RemainingHours,
    decimal RemainingDays);

/// <summary>
/// One account (weeks 1-5 or week 6). "Available" counts only the buckets that may be used today;
/// "Total" also counts buckets that are earned but not usable yet (week 6 earned this year).
/// </summary>
public record AccountBalanceDto(
    string Account,
    string Name,
    decimal AvailableHours,
    decimal AvailableDays,
    decimal TotalHours,
    decimal TotalDays,
    BucketBalanceDto[] Buckets);