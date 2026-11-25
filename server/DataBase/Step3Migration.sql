-- Run this ONCE on a database that already has the version 2 schema, to add what step 3 needs
-- WITHOUT losing your data. (A fresh database gets all of this from SQL.sql.)

-- One accrual row per bucket per month
CREATE UNIQUE INDEX IF NOT EXISTS uq_ledger_accrual_month
    ON ledger_entry (bucket_id, entry_date) WHERE entry_type = 'ACCRUAL';

-- Two new rule parameters, in the presets and in the live global rules
INSERT INTO rule_preset_value (preset_id, param_key, param_value, value_type) VALUES
                                                                                  (1,'PARTIAL_MONTH_DIVISOR','30','DECIMAL'),
                                                                                  (1,'W15_ROUND_WHOLE_DAYS_AT_PERIOD_END','true','BOOL'),
                                                                                  (2,'PARTIAL_MONTH_DIVISOR','30','DECIMAL'),
                                                                                  (2,'W15_ROUND_WHOLE_DAYS_AT_PERIOD_END','true','BOOL')
    ON CONFLICT DO NOTHING;

INSERT INTO rule_parameter (param_key, param_value, value_type, effective_from, source_preset_id)
SELECT param_key, param_value, value_type, DATE '2020-09-01', 2
FROM   rule_preset_value
WHERE  preset_id = 2
  AND  param_key IN ('PARTIAL_MONTH_DIVISOR', 'W15_ROUND_WHOLE_DAYS_AT_PERIOD_END')
    ON CONFLICT DO NOTHING;