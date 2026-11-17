-- =====================================================================
-- Business Esbjerg: Absence Resource Planning
-- PostgreSQL schema (PostgreSQL 14+; works locally and on Render)
--
-- Conventions
--   * Balances are NEVER stored. They are SUM(ledger_entry.hours).
--   * All balances are in HOURS. Weeks 1-5 also keep `days` for display.
--   * Percentages are stored as plain numbers (12.5 = 12.5 %).
--   * Rules are parameters with effective_from; rows are superseded,
--     never overwritten.
--   * "Enum" columns are VARCHAR + CHECK (simple to map from C#).
-- =====================================================================

-- ---------------------------------------------------------------------
-- 0. RESET - DELETES ALL DATA IN THESE TABLES. Lets you re-run the script.
--    Remove this section once you have data you want to keep.
-- ---------------------------------------------------------------------
DROP VIEW IF EXISTS v_company_liability;
DROP VIEW IF EXISTS v_liability;
DROP VIEW IF EXISTS v_bucket_balance;
DROP TABLE IF EXISTS
    audit_log, liability_snapshot, holiday_obstacle, ledger_entry, payout,
    transfer_agreement, holiday_request_day, holiday_request, holiday_bucket,
    holiday_account_type, calendar_day, rule_parameter, employee_month,
    work_schedule_day, work_schedule, employment_terms, employee,
    rule_set, rule_preset_value, rule_preset
CASCADE;

-- ---------------------------------------------------------------------
-- 1. RULES AND PARAMETERS
-- ---------------------------------------------------------------------

CREATE TABLE rule_preset (
    preset_id     INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name          VARCHAR(100) NOT NULL UNIQUE,
    description   VARCHAR(500)
);

CREATE TABLE rule_preset_value (
    preset_id     INT          NOT NULL REFERENCES rule_preset (preset_id) ON DELETE CASCADE,
    param_key     VARCHAR(60)  NOT NULL,
    param_value   VARCHAR(100) NOT NULL,
    value_type    VARCHAR(10)  NOT NULL DEFAULT 'TEXT'
                  CHECK (value_type IN ('INT','DECIMAL','TEXT','DATE','BOOL')),
    PRIMARY KEY (preset_id, param_key)
);

-- A rule set is what an employee is actually assigned to.
-- Changing a value after copying from a preset makes it custom.
CREATE TABLE rule_set (
    rule_set_id         INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name                VARCHAR(100) NOT NULL UNIQUE,
    based_on_preset_id  INT NULL REFERENCES rule_preset (preset_id),
    is_custom           BOOLEAN NOT NULL DEFAULT FALSE
);

-- ---------------------------------------------------------------------
-- 2. PEOPLE AND EMPLOYMENT
-- ---------------------------------------------------------------------

CREATE TABLE employee (
    employee_id         INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    employee_no         VARCHAR(20)  NOT NULL UNIQUE,
    name                VARCHAR(150) NOT NULL,
    email               VARCHAR(255) NOT NULL,
    password_hash       VARCHAR(100) NULL,               -- BCrypt hash, never the real password
    role                VARCHAR(20)  NOT NULL DEFAULT 'EMPLOYEE'
                        CHECK (role IN ('EMPLOYEE','ACCOUNTING','ADMIN')),
    manager_id          INT NULL REFERENCES employee (employee_id),
    hire_date           DATE NOT NULL,
    termination_date    DATE NULL,
    termination_reason  VARCHAR(20) NULL
                        CHECK (termination_reason IN ('RESIGNATION','DISMISSAL','RETIREMENT','DEATH','OTHER')),
    CONSTRAINT chk_employee_term CHECK (termination_date IS NULL OR termination_date >= hire_date)
);
-- Postgres compares text case-sensitively, so make emails unique ignoring case
CREATE UNIQUE INDEX uq_employee_email_lower ON employee (lower(email));

-- History table: one row per change of pay type / fraction / rule set
CREATE TABLE employment_terms (
    terms_id             INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    employee_id          INT NOT NULL REFERENCES employee (employee_id),
    valid_from           DATE NOT NULL,
    valid_to             DATE NULL,
    pay_type             VARCHAR(10) NOT NULL DEFAULT 'SALARIED' CHECK (pay_type IN ('SALARIED','HOURLY')),
    employment_fraction  NUMERIC(4,3) NOT NULL DEFAULT 1.000,   -- 1.000 = full time
    rule_set_id          INT NOT NULL REFERENCES rule_set (rule_set_id),
    CONSTRAINT uq_terms_start UNIQUE (employee_id, valid_from),
    CONSTRAINT chk_terms_fraction CHECK (employment_fraction > 0 AND employment_fraction <= 1),
    CONSTRAINT chk_terms_dates CHECK (valid_to IS NULL OR valid_to >= valid_from)
);

CREATE TABLE work_schedule (
    schedule_id   INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    employee_id   INT NOT NULL REFERENCES employee (employee_id),
    valid_from    DATE NOT NULL,
    valid_to      DATE NULL,
    CONSTRAINT uq_schedule_start UNIQUE (employee_id, valid_from),
    CONSTRAINT chk_schedule_dates CHECK (valid_to IS NULL OR valid_to >= valid_from)
);

-- weekday: 1 = Monday ... 7 = Sunday
CREATE TABLE work_schedule_day (
    schedule_id   INT NOT NULL REFERENCES work_schedule (schedule_id) ON DELETE CASCADE,
    weekday       SMALLINT NOT NULL CHECK (weekday BETWEEN 1 AND 7),
    hours         NUMERIC(4,2) NOT NULL CHECK (hours >= 0 AND hours <= 24),
    PRIMARY KEY (schedule_id, weekday)
);

-- Monthly facts needed for accrual and DKK liability
CREATE TABLE employee_month (
    employee_id                INT NOT NULL REFERENCES employee (employee_id),
    year                       SMALLINT NOT NULL,
    month                      SMALLINT NOT NULL CHECK (month BETWEEN 1 AND 12),
    days_employed              SMALLINT NOT NULL CHECK (days_employed BETWEEN 0 AND 31),
    employment_fraction        NUMERIC(4,3) NOT NULL CHECK (employment_fraction >= 0 AND employment_fraction <= 1),
    holiday_qualifying_salary  NUMERIC(12,2) NOT NULL DEFAULT 0,  -- ferieberettiget loen
    PRIMARY KEY (employee_id, year, month)
);

CREATE TABLE rule_parameter (
    parameter_id   INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    rule_set_id    INT NOT NULL REFERENCES rule_set (rule_set_id),
    param_key      VARCHAR(60)  NOT NULL,
    param_value    VARCHAR(100) NOT NULL,
    value_type     VARCHAR(10)  NOT NULL DEFAULT 'TEXT'
                   CHECK (value_type IN ('INT','DECIMAL','TEXT','DATE','BOOL')),
    effective_from DATE NOT NULL,
    effective_to   DATE NULL,
    created_by     INT NULL REFERENCES employee (employee_id),
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_param_version UNIQUE (rule_set_id, param_key, effective_from),
    CONSTRAINT chk_param_dates CHECK (effective_to IS NULL OR effective_to >= effective_from)
);

-- Public holidays and company days off (never count as holiday days)
CREATE TABLE calendar_day (
    date      DATE PRIMARY KEY,
    name      VARCHAR(100) NOT NULL,
    day_type  VARCHAR(20) NOT NULL CHECK (day_type IN ('PUBLIC_HOLIDAY','COMPANY_DAY_OFF'))
);

-- ---------------------------------------------------------------------
-- 3. ACCOUNTS, BUCKETS, REQUESTS, TRANSFERS, PAYOUTS, LEDGER
-- ---------------------------------------------------------------------

CREATE TABLE holiday_account_type (
    account_type_id  INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code             VARCHAR(30) NOT NULL UNIQUE,     -- WEEKS_1_5 / WEEK_6
    name             VARCHAR(100) NOT NULL,
    native_unit      VARCHAR(10) NOT NULL CHECK (native_unit IN ('DAYS','HOURS'))
);

-- One bucket = one account for one earning period
CREATE TABLE holiday_bucket (
    bucket_id          INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    employee_id        INT NOT NULL REFERENCES employee (employee_id),
    account_type_id    INT NOT NULL REFERENCES holiday_account_type (account_type_id),
    earning_start      DATE NOT NULL,
    earning_end        DATE NOT NULL,
    usage_start        DATE NOT NULL,
    usage_end          DATE NOT NULL,
    transfer_deadline  DATE NULL,
    status             VARCHAR(10) NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','EXPIRED','SETTLED')),
    CONSTRAINT uq_bucket UNIQUE (employee_id, account_type_id, earning_start),
    CONSTRAINT chk_bucket_earning CHECK (earning_end >= earning_start),
    CONSTRAINT chk_bucket_usage   CHECK (usage_end >= usage_start)
);
CREATE INDEX idx_bucket_expiry ON holiday_bucket (usage_end, status);

CREATE TABLE holiday_request (
    request_id                     INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    employee_id                    INT NOT NULL REFERENCES employee (employee_id),
    account_type_id                INT NOT NULL REFERENCES holiday_account_type (account_type_id),
    start_date                     DATE NOT NULL,
    end_date                       DATE NOT NULL,
    is_main_holiday                BOOLEAN NOT NULL DEFAULT FALSE,
    status                         VARCHAR(30) NOT NULL DEFAULT 'SUBMITTED'
                                   CHECK (status IN ('SUBMITTED','APPROVED','REJECTED',
                                                     'CANCELLED_BY_EMPLOYEE','CANCELLED_BY_EMPLOYER')),
    total_hours                    NUMERIC(7,2) NOT NULL DEFAULT 0,   -- snapshot
    submitted_at                   TIMESTAMPTZ NOT NULL DEFAULT now(),
    decided_by                     INT NULL REFERENCES employee (employee_id),
    decided_at                     TIMESTAMPTZ NULL,
    decision_note                  VARCHAR(500),
    cancel_reason                  VARCHAR(500),
    employer_loss_compensation_dkk NUMERIC(10,2) NULL,
    CONSTRAINT chk_request_dates CHECK (end_date >= start_date)
);
CREATE INDEX idx_request_employee ON holiday_request (employee_id, start_date);
CREATE INDEX idx_request_status   ON holiday_request (status);

-- One row per calendar day in the request (weekends/public holidays = 0 hours)
CREATE TABLE holiday_request_day (
    request_id      INT NOT NULL REFERENCES holiday_request (request_id) ON DELETE CASCADE,
    date            DATE NOT NULL,
    hours_deducted  NUMERIC(4,2) NOT NULL DEFAULT 0 CHECK (hours_deducted >= 0),
    bucket_id       INT NULL REFERENCES holiday_bucket (bucket_id),   -- NULL when 0 hours are deducted
    PRIMARY KEY (request_id, date)
);

-- Written agreements to move the 5th week / week 6 to the next period
CREATE TABLE transfer_agreement (
    transfer_id     INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    employee_id     INT NOT NULL REFERENCES employee (employee_id),
    from_bucket_id  INT NOT NULL REFERENCES holiday_bucket (bucket_id),
    to_bucket_id    INT NOT NULL REFERENCES holiday_bucket (bucket_id),
    hours           NUMERIC(7,2) NOT NULL CHECK (hours > 0),
    agreed_on       DATE NOT NULL,
    document_ref    VARCHAR(255),
    CONSTRAINT chk_transfer_diff CHECK (from_bucket_id <> to_bucket_id)
);

-- Payouts are triggered by situations, never requested by employees
CREATE TABLE payout (
    payout_id       INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    employee_id     INT NOT NULL REFERENCES employee (employee_id),
    bucket_id       INT NOT NULL REFERENCES holiday_bucket (bucket_id),
    trigger_type    VARCHAR(20) NOT NULL
                    CHECK (trigger_type IN ('TERMINATION','WEEK5_EXPIRY','WEEK6_EXPIRY',
                                            'OBSTACLE','RETIREMENT','DEATH')),
    hours           NUMERIC(7,2) NOT NULL CHECK (hours > 0),
    amount_dkk      NUMERIC(12,2) NOT NULL CHECK (amount_dkk >= 0),
    supplement_dkk  NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (supplement_dkk >= 0),
    status          VARCHAR(10) NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','APPROVED','PAID')),
    payroll_month   DATE NULL,                      -- first day of the payroll month
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    processed_by    INT NULL REFERENCES employee (employee_id)
);

-- The heart of the system: signed rows, balance = SUM(hours)
CREATE TABLE ledger_entry (
    entry_id     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    bucket_id    INT NOT NULL REFERENCES holiday_bucket (bucket_id),
    entry_type   VARCHAR(20) NOT NULL
                 CHECK (entry_type IN ('ACCRUAL','USAGE','TRANSFER_OUT','TRANSFER_IN',
                                       'PAYOUT','ADJUSTMENT','REVERSAL')),
    entry_date   DATE NOT NULL,
    hours        NUMERIC(8,3) NOT NULL,             -- signed
    days         NUMERIC(6,2) NULL,                 -- weeks 1-5 display only
    request_id   INT NULL REFERENCES holiday_request (request_id),
    payout_id    INT NULL REFERENCES payout (payout_id),
    transfer_id  INT NULL REFERENCES transfer_agreement (transfer_id),
    note         VARCHAR(500),
    created_by   INT NULL REFERENCES employee (employee_id),   -- NULL = system job
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    -- sign rules (ADJUSTMENT and REVERSAL may go either way)
    CONSTRAINT chk_ledger_sign CHECK (
        (entry_type IN ('ACCRUAL','TRANSFER_IN') AND hours > 0) OR
        (entry_type IN ('USAGE','TRANSFER_OUT','PAYOUT') AND hours < 0) OR
        (entry_type IN ('ADJUSTMENT','REVERSAL'))
    ),
    CONSTRAINT chk_ledger_link CHECK (
        (entry_type <> 'USAGE'  OR request_id  IS NOT NULL) AND
        (entry_type <> 'PAYOUT' OR payout_id   IS NOT NULL) AND
        (entry_type NOT IN ('TRANSFER_IN','TRANSFER_OUT') OR transfer_id IS NOT NULL)
    )
);
CREATE INDEX idx_ledger_bucket ON ledger_entry (bucket_id, entry_date);

-- ---------------------------------------------------------------------
-- 4. SPECIAL CASES, SNAPSHOTS AND AUDIT
-- ---------------------------------------------------------------------

CREATE TABLE holiday_obstacle (
    obstacle_id               INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    employee_id               INT NOT NULL REFERENCES employee (employee_id),
    obstacle_type             VARCHAR(30) NOT NULL
                              CHECK (obstacle_type IN ('ILLNESS_BEFORE_HOLIDAY','ILLNESS_DURING_HOLIDAY',
                                                       'PARENTAL_LEAVE','STRIKE','OTHER')),
    start_date                DATE NOT NULL,
    end_date                  DATE NULL,
    documentation_ref         VARCHAR(255),
    related_request_id        INT NULL REFERENCES holiday_request (request_id),
    replacement_hours_granted NUMERIC(7,2) NOT NULL DEFAULT 0,
    CONSTRAINT chk_obstacle_dates CHECK (end_date IS NULL OR end_date >= start_date)
);

-- Historical liability reporting ("what did we owe on 31 Dec?")
CREATE TABLE liability_snapshot (
    snapshot_date    DATE NOT NULL,
    employee_id      INT NOT NULL REFERENCES employee (employee_id),
    bucket_id        INT NOT NULL REFERENCES holiday_bucket (bucket_id),
    remaining_hours  NUMERIC(8,3)  NOT NULL,
    salary_basis     NUMERIC(12,2) NOT NULL,
    pct_used         NUMERIC(6,3)  NOT NULL,
    amount_dkk       NUMERIC(12,2) NOT NULL,
    PRIMARY KEY (snapshot_date, bucket_id)
);

CREATE TABLE audit_log (
    log_id      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    table_name  VARCHAR(60) NOT NULL,
    row_id      VARCHAR(60) NOT NULL,
    action      VARCHAR(10) NOT NULL CHECK (action IN ('INSERT','UPDATE','DELETE')),
    old_value   JSONB NULL,
    new_value   JSONB NULL,
    changed_by  INT NULL REFERENCES employee (employee_id),
    changed_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_audit_row ON audit_log (table_name, row_id);

-- ---------------------------------------------------------------------
-- 5. VIEWS
-- ---------------------------------------------------------------------

-- Remaining and earned hours per bucket
CREATE VIEW v_bucket_balance AS
SELECT  b.bucket_id,
        b.employee_id,
        b.account_type_id,
        b.earning_start,
        b.earning_end,
        b.usage_end,
        b.status,
        COALESCE(SUM(l.hours), 0)                                              AS remaining_hours,
        COALESCE(SUM(CASE WHEN l.entry_type = 'ACCRUAL' THEN l.hours END), 0)  AS earned_hours
FROM    holiday_bucket b
LEFT JOIN ledger_entry l ON l.bucket_id = b.bucket_id
GROUP BY b.bucket_id;

-- DKK liability per bucket as of today
--   weeks 1-5: PAYOUT_PCT_W15 % of qualifying salary in earning period
--   week 6:    PAYOUT_PCT_W6  % of qualifying salary in earning period
--   times      remaining_hours / earned_hours
-- The percentage is read from rule_parameter (latest row effective today)
-- through the employee's current employment_terms -> rule_set.
-- Unpaid ferietillaeg is not included here; add it in the application.
CREATE VIEW v_liability AS
SELECT  x.bucket_id,
        x.employee_id,
        x.code AS account_code,
        x.remaining_hours,
        x.earned_hours,
        x.salary_basis,
        x.pct,
        CASE WHEN x.earned_hours > 0
             THEN ROUND(x.salary_basis * (x.pct / 100) * (x.remaining_hours / x.earned_hours), 2)
             ELSE 0 END AS liability_dkk
FROM (
    SELECT  vb.bucket_id,
            vb.employee_id,
            t.code,
            vb.remaining_hours,
            vb.earned_hours,
            COALESCE((
                SELECT SUM(m.holiday_qualifying_salary)
                FROM   employee_month m
                WHERE  m.employee_id = vb.employee_id
                  AND  make_date(m.year, m.month, 1) BETWEEN vb.earning_start AND vb.earning_end
            ), 0) AS salary_basis,
            COALESCE((
                SELECT CAST(rp.param_value AS NUMERIC(6,3))
                FROM   employment_terms et
                JOIN   rule_parameter rp
                       ON  rp.rule_set_id = et.rule_set_id
                       AND rp.param_key = CASE t.code WHEN 'WEEKS_1_5' THEN 'PAYOUT_PCT_W15'
                                                      ELSE 'PAYOUT_PCT_W6' END
                       AND rp.effective_from <= CURRENT_DATE
                WHERE  et.employee_id = vb.employee_id
                  AND  et.valid_from <= CURRENT_DATE
                  AND (et.valid_to IS NULL OR et.valid_to >= CURRENT_DATE)
                ORDER BY rp.effective_from DESC
                LIMIT 1
            ), 0) AS pct
    FROM    v_bucket_balance vb
    JOIN    holiday_account_type t ON t.account_type_id = vb.account_type_id
    WHERE   vb.status = 'OPEN'
) x;

-- Company-wide total
CREATE VIEW v_company_liability AS
SELECT  COUNT(DISTINCT employee_id) AS employees,
        SUM(remaining_hours)        AS remaining_hours,
        SUM(liability_dkk)          AS liability_dkk
FROM    v_liability;

-- ---------------------------------------------------------------------
-- 6. SEED DATA
-- (ids start at 1 because the tables were just recreated above)
-- ---------------------------------------------------------------------

INSERT INTO holiday_account_type (code, name, native_unit) VALUES
 ('WEEKS_1_5', 'Weeks 1-5 (Ferieloven, samtidighedsferie)', 'DAYS'),
 ('WEEK_6',    '6th holiday week (KL 05.11, forskudt)',      'HOURS');

INSERT INTO rule_preset (name, description) VALUES
 ('Ferieloven (minimum)',                  '5 weeks, nothing else'),
 ('Ferieloven + 6. ferieuge (KL 05.11)',   'Model used by Business Esbjerg'),
 ('Ferieloven + feriefridage',             'Overenskomst with 5 extra holiday days'),
 ('Hourly-paid staff',                     'Feriegodtgoerelse 12.5% instead of salary during holiday');

-- Preset 1: minimum
INSERT INTO rule_preset_value (preset_id, param_key, param_value, value_type) VALUES
 (1,'W15_DAYS_PER_MONTH','2.08','DECIMAL'),
 (1,'W15_EARNING_START','09-01','TEXT'),
 (1,'W15_USAGE_END_MONTHS_AFTER_EARNING','16','INT'),
 (1,'MAIN_HOLIDAY_DAYS','15','INT'),
 (1,'NOTICE_MAIN_MONTHS','3','INT'),
 (1,'NOTICE_OTHER_MONTHS','1','INT'),
 (1,'W6_ENABLED','false','BOOL'),
 (1,'PAYOUT_PCT_W15','12.5','DECIMAL');

-- Preset 2: KL 05.11 model (Business Esbjerg)
INSERT INTO rule_preset_value (preset_id, param_key, param_value, value_type) VALUES
 (2,'W15_DAYS_PER_MONTH','2.08','DECIMAL'),
 (2,'W15_EARNING_START','09-01','TEXT'),
 (2,'W15_USAGE_END_MONTHS_AFTER_EARNING','16','INT'),
 (2,'MAIN_HOLIDAY_DAYS','15','INT'),
 (2,'MAIN_HOLIDAY_WINDOW_START','05-01','TEXT'),
 (2,'MAIN_HOLIDAY_WINDOW_END','09-30','TEXT'),
 (2,'NOTICE_MAIN_MONTHS','3','INT'),
 (2,'NOTICE_OTHER_MONTHS','1','INT'),
 (2,'W6_ENABLED','true','BOOL'),
 (2,'W6_HOURS_PER_MONTH','3.083','DECIMAL'),
 (2,'W6_EARNING_START','01-01','TEXT'),
 (2,'W6_USAGE_START','05-01','TEXT'),
 (2,'W6_USAGE_END','04-30','TEXT'),
 (2,'W5_TRANSFER_DEADLINE','12-31','TEXT'),
 (2,'W6_TRANSFER_DEADLINE','09-30','TEXT'),
 (2,'REQUEST_UNIT','DAYS','TEXT'),
 (2,'PAYOUT_PCT_W15','12.5','DECIMAL'),
 (2,'PAYOUT_PCT_W6','2.5','DECIMAL'),
 (2,'SUPPLEMENT_PCT_BASE','1.00','DECIMAL'),
 (2,'SUPPLEMENT_PCT_RAISED','1.48','DECIMAL');

-- Presets 3 and 4: starting points (admin fills in the rest)
INSERT INTO rule_preset_value (preset_id, param_key, param_value, value_type) VALUES
 (3,'W15_DAYS_PER_MONTH','2.08','DECIMAL'),
 (3,'FERIEFRIDAGE_DAYS_PER_YEAR','5','INT'),
 (4,'HOLIDAY_PAY_MODE','CASH_PERCENT','TEXT'),
 (4,'PAYOUT_PCT_W15','12.5','DECIMAL'),
 (4,'PAYOUT_PCT_W6','2.5','DECIMAL');

-- Business Esbjerg's active rule set, copied from preset 2
INSERT INTO rule_set (name, based_on_preset_id, is_custom)
VALUES ('Business Esbjerg standard', 2, FALSE);

INSERT INTO rule_parameter (rule_set_id, param_key, param_value, value_type, effective_from)
SELECT 1, param_key, param_value, value_type, DATE '2020-09-01'
FROM   rule_preset_value
WHERE  preset_id = 2
  AND  param_key <> 'SUPPLEMENT_PCT_RAISED';

-- Supplement history: shows why effective_from matters
INSERT INTO rule_parameter (rule_set_id, param_key, param_value, value_type, effective_from, effective_to) VALUES
 (1,'SUPPLEMENT_PCT_RAISED','0.95','DECIMAL','2020-09-01','2024-04-30'),
 (1,'SUPPLEMENT_PCT_RAISED','1.48','DECIMAL','2024-05-01', NULL);

-- Work schedule (hours per weekday) is stored per employee in
-- work_schedule / work_schedule_day. Standard full-time pattern:
--   Mon-Thu 7.5, Fri 7, Sat-Sun 0  (= 37 hours/week)

-- Sample public holidays (extend with the full Danish calendar)
INSERT INTO calendar_day (date, name, day_type) VALUES
 ('2026-12-24', 'Juleaftensdag',   'COMPANY_DAY_OFF'),
 ('2026-12-25', '1. juledag',      'PUBLIC_HOLIDAY'),
 ('2026-12-26', '2. juledag',      'PUBLIC_HOLIDAY'),
 ('2026-12-31', 'Nytaarsaftensdag','COMPANY_DAY_OFF'),
 ('2027-01-01', 'Nytaarsdag',      'PUBLIC_HOLIDAY');

-- ---------------------------------------------------------------------
-- 7. USEFUL QUERIES
-- ---------------------------------------------------------------------
-- Balance per bucket for one employee:
--   SELECT * FROM v_bucket_balance WHERE employee_id = 1;
--
-- Buckets expiring within 90 days that still have hours left:
--   SELECT * FROM v_bucket_balance
--   WHERE status = 'OPEN' AND remaining_hours > 0
--     AND usage_end <= CURRENT_DATE + INTERVAL '90 days';
--
-- Company liability in DKK:
--   SELECT * FROM v_company_liability;