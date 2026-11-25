using System.Globalization;
using Infrastructure;
using LinqToDB;
using LinqToDB.Async;

namespace Service;

/// <summary>
/// Reads the global rule parameters. Every lookup asks "what was the value ON THIS DATE?",
/// so a rule that changes later never affects calculations for earlier dates.
/// </summary>
public class RuleBook
{
    // key -> list of (effective from, value), oldest first
    private readonly Dictionary<string, List<(DateOnly EffectiveFrom, string Value)>> _values;

    private RuleBook(Dictionary<string, List<(DateOnly EffectiveFrom, string Value)>> values) => _values = values;

    public static async Task<RuleBook> LoadAsync(ExamDatabase db)
    {
        var rows = await db.RuleParameters.ToListAsync();

        var values = rows
            .GroupBy(r => r.ParamKey)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.EffectiveFrom)
                      .Select(r => (DateOnly.FromDateTime(r.EffectiveFrom), r.ParamValue))
                      .ToList());

        return new RuleBook(values);
    }

    /// <summary>The value in force on that date, or null if the rule does not exist (yet) on that date.</summary>
    public string? Find(string key, DateOnly on)
    {
        if (!_values.TryGetValue(key, out var list)) return null;

        string? found = null;
        foreach (var (effectiveFrom, value) in list)
        {
            if (effectiveFrom <= on) found = value;
            else break;
        }
        return found;
    }

    public string Text(string key, DateOnly on) =>
        Find(key, on) ?? throw new InvalidOperationException($"Rule parameter {key} has no value on {on:yyyy-MM-dd}.");

    public decimal Decimal(string key, DateOnly on, decimal? fallback = null)
    {
        var text = Find(key, on);
        if (text is null)
            return fallback ?? throw new InvalidOperationException($"Rule parameter {key} has no value on {on:yyyy-MM-dd}.");
        return decimal.Parse(text, CultureInfo.InvariantCulture);
    }

    public int Int(string key, DateOnly on) => int.Parse(Text(key, on), CultureInfo.InvariantCulture);

    public bool Bool(string key, DateOnly on, bool? fallback = null)
    {
        var text = Find(key, on);
        if (text is null)
            return fallback ?? throw new InvalidOperationException($"Rule parameter {key} has no value on {on:yyyy-MM-dd}.");
        return bool.Parse(text);
    }

    /// <summary>A "MM-DD" rule such as "09-01" becomes (9, 1).</summary>
    public (int Month, int Day) MonthDay(string key, DateOnly on)
    {
        var parts = Text(key, on).Split('-');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }
}