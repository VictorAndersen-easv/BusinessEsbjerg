using LinqToDB;
using LinqToDB.Mapping;

namespace Infrastructure.Entities;

/// <summary>
/// One global rule value (like W15_DAYS_PER_MONTH = 2.08) that applies from EffectiveFrom.
/// To change a rule you ADD a row with a later date. Old rows stay, so past years keep their old values.
/// </summary>
[Table("rule_parameter")]
public class RuleParameter
{
    [PrimaryKey, Identity, Column("parameter_id")]
    public int ParameterId { get; set; }

    [Column("param_key")]
    public string ParamKey { get; set; } = "";

    [Column("param_value")]
    public string ParamValue { get; set; } = "";

    [Column("value_type")]
    public string ValueType { get; set; } = "TEXT";

    [Column("effective_from", DataType = DataType.Date)]
    public DateTime EffectiveFrom { get; set; }

    [Column("source_preset_id")]
    public int? SourcePresetId { get; set; }

    [Column("created_by")]
    public int? CreatedBy { get; set; }

    [Column("created_at", SkipOnInsert = true)]
    public DateTime CreatedAt { get; set; }
}