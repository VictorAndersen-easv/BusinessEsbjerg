-- =====================================================================
-- Business Esbjerg: Absence Resource Planning
-- PostgreSQL schema, version 2 (PostgreSQL 14+; works locally and on Render)
--
-- Conventions
--   * Balances are NEVER stored. They are SUM() over ledger_entry rows.
--   * Hours (two decimals) are the unit the balance is kept in; every ledger
--     row ALSO carries days, because holiday is a right to days off.
--   * The system holds NO money amounts. Payroll handles salary and pay.
--   * Rules are global parameters with effective_from. A new value never
--     rewrites history; the ledger keeps the amounts that were booked.
--   * "Enum" columns are VARCHAR + CHECK (simple to map from C#).
-- =====================================================================

-- Needed for the "no overlapping periods" constraints below
CREATE EXTENSION IF NOT EXISTS btree_gist;

-- ---------------------------------------------------------------------
-- 0. RESET - DELETES ALL DATA IN THESE TABLES. Lets you re-run the script.
--    Remove this section once you have data you want to keep.
-- ---------------------------------------------------------------------
DROP VIEW IF EXISTS v_company_liability;
DROP VIEW IF EXISTS v_liability;
DROP VIEW IF EXISTS v_bucket_balance;
DROP VIEW IF EXISTS v_schedule_summary;
DROP TABLE IF EXISTS
    audit_log, holiday_obstacle, ledger_entry, payout, transfer_request,
    transfer_agreement, holiday_request_day, holiday_request, holiday_bucket,
    holiday_account_type, calendar_day, rule_parameter, rule_preset_value,
    rule_preset, rule_set, employment_terms, employee_month, liability_snapshot,
    work_schedule_day, work_schedule, user_account, employee
    CASCADE;
DROP FUNCTION IF EXISTS forbid_ledger_change() CASCADE;

-- ---------------------------------------------------------------------
-- 1. PEOPLE AND LOGINS
-- ---------------------------------------------------------------------

-- A person who earns holiday. (Logins are in user_account.)
CREATE TABLE employee (
                          employee_id         INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                          employee_no         VARCHAR(20)  NOT NULL UNIQUE,
                          name                VARCHAR(150) NOT NULL,
                          hire_date           DATE NOT NULL,
                          termination_date    DATE NULL,
                          termination_reason  VARCHAR(20) NULL
                        CHECK (termination_reason IN ('RESIGNATION','DISMISSAL','RETIREMENT','DEATH','OTHER')),
                          CONSTRAINT chk_employee_term CHECK (termination_date IS NULL OR termination_date >= hire_date)
);

-- A login. EMPLOYEE logins belong to one employee. The accountant and the system
-- owner are separate logins that belong to no employee (the person behind them
-- also has their own EMPLOYEE login).
CREATE TABLE user_account (
                              account_id     INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                              email          VARCHAR(255) NOT NULL,            -- name@businessesbjerg.com (checked by the app)
                              password_hash  VARCHAR(100) NOT NULL,            -- BCrypt hash, never the real password
                              role           VARCHAR(20)  NOT NULL
                                  CHECK (role IN ('EMPLOYEE','ACCOUNTANT','SYSTEM_OWNER')),
                              employee_id    INT NULL UNIQUE REFERENCES employee (employee_id),   -- one login per employee
                              display_name   VARCHAR(150) NULL,                -- only for the two role logins
                              created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
                              CONSTRAINT chk_account_role_link CHECK (
                                  (role = 'EMPLOYEE'  AND employee_id IS NOT NULL) OR
                                  (role <> 'EMPLOYEE' AND employee_id IS NULL AND display_name IS NOT NULL)
                                  )
);
-- Emails are unique ignoring case (Postgres is case-sensitive by default)
CREATE UNIQUE INDEX uq_user_account_email_lower ON user_account (lower(email));
-- Exactly one accountant login and one system owner login: "at most one" is enforced here,
-- "at least one" is made sure of by the startup seeding.
CREATE UNIQUE INDEX uq_one_accountant   ON user_account (role) WHERE role = 'ACCOUNTANT';
CREATE UNIQUE INDEX uq_one_system_owner ON user_account (role) WHERE role = 'SYSTEM_OWNER';

-- ---------------------------------------------------------------------
-- 2. GLOBAL RULES
-- ---------------------------------------------------------------------

-- Templates the system owner can apply (copy into rule_parameter)
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

-- The live global rules. Lookup: the row with the latest effective_from <= the date in question.
-- Rows are only ever added, never changed, so past calculations stay explainable.
CREATE TABLE rule_parameter (
                                parameter_id      INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                                param_key         VARCHAR(60)  NOT NULL,
                                param_value       VARCHAR(100) NOT NULL,
                                value_type        VARCHAR(10)  NOT NULL DEFAULT 'TEXT'
                                    CHECK (value_type IN ('INT','DECIMAL','TEXT','DATE','BOOL')),
                                effective_from    DATE NOT NULL,
                                source_preset_id  INT NULL REFERENCES rule_preset (preset_id),   -- NULL = custom value
                                created_by        INT NULL REFERENCES user_account (account_id),
                                created_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
                                CONSTRAINT uq_param_version UNIQUE (param_key, effective_from)
);

-- Public holidays and company days off (never count as holiday days)
CREATE TABLE calendar_day (
                              date      DATE PRIMARY KEY,
                              name      VARCHAR(100) NOT NULL,
                              day_type  VARCHAR(20) NOT NULL CHECK (day_type IN ('PUBLIC_HOLIDAY','COMPANY_DAY_OFF'))
);

-- ---------------------------------------------------------------------
-- 3. WORKING SCHEDULES (full history, whole weeks, no overlaps)
-- ---------------------------------------------------------------------

CREATE TABLE work_schedule (
                               schedule_id   INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                               employee_id   INT NOT NULL REFERENCES employee (employee_id),
                               valid_from    DATE NOT NULL,                      -- always a Monday
                               valid_to      DATE NULL,                          -- always a Sunday; NULL = still in use
                               created_by    INT NULL REFERENCES user_account (account_id),
                               created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
                               CONSTRAINT chk_schedule_monday CHECK (EXTRACT(ISODOW FROM valid_from) = 1),
                               CONSTRAINT chk_schedule_sunday CHECK (valid_to IS NULL OR
                                                                     (EXTRACT(ISODOW FROM valid_to) = 7 AND valid_to >= valid_from + 6)),
    -- one employee cannot have two schedules covering the same day
                               CONSTRAINT excl_schedule_overlap EXCLUDE USING gist (
        employee_id WITH =,
        daterange(valid_from, valid_to, '[]') WITH &&
    )
);

-- Five rows per schedule (Monday-Friday). A day off has no times and 0 hours.
CREATE TABLE work_schedule_day (
                                   schedule_id   INT NOT NULL REFERENCES work_schedule (schedule_id) ON DELETE CASCADE,
                                   weekday       SMALLINT NOT NULL CHECK (weekday BETWEEN 1 AND 5),   -- 1 = Monday ... 5 = Friday
                                   start_time    TIME NULL,
                                   end_time      TIME NULL,
                                   hours         NUMERIC(4,2) NOT NULL,              -- end minus start, two decimals
                                   PRIMARY KEY (schedule_id, weekday),
                                   CONSTRAINT chk_scheduleday_hours CHECK (
                                       (start_time IS NULL AND end_time IS NULL AND hours = 0)
                                           OR
                                       (start_time IS NOT NULL AND end_time IS NOT NULL AND end_time > start_time
                                           AND hours = ROUND(EXTRACT(EPOCH FROM (end_time - start_time)) / 3600.0, 2))
                                       )
);

-- Weekly hours and the average length of a working day for each schedule
-- (used when a schedule change converts the balance and for week-6 day equivalents)
CREATE VIEW v_schedule_summary AS
SELECT  s.schedule_id,
        s.employee_id,
        s.valid_from,
        s.valid_to,
        SUM(d.hours)                                AS weekly_hours,
        COUNT(*) FILTER (WHERE d.hours > 0)         AS working_days,
    CASE WHEN COUNT(*) FILTER (WHERE d.hours > 0) > 0
             THEN ROUND(SUM(d.hours) / COUNT(*) FILTER (WHERE d.hours > 0), 2)
         ELSE 0 END                             AS avg_day_hours
FROM    work_schedule s
            JOIN    work_schedule_day d ON d.schedule_id = s.schedule_id
GROUP BY s.schedule_id;

-- ---------------------------------------------------------------------
-- 4. ACCOUNTS, BUCKETS, REQUESTS, TRANSFERS, PAYOUTS, LEDGER
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
                                 request_id                 INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                                 employee_id                INT NOT NULL REFERENCES employee (employee_id),
                                 account_type_id            INT NOT NULL REFERENCES holiday_account_type (account_type_id),
                                 start_date                 DATE NOT NULL,
                                 end_date                   DATE NOT NULL,
                                 is_main_holiday            BOOLEAN NOT NULL DEFAULT FALSE,
                                 status                     VARCHAR(30) NOT NULL DEFAULT 'SUBMITTED'
                                     CHECK (status IN ('SUBMITTED','APPROVED','REJECTED',
                                                       'CANCELLED_BY_EMPLOYEE',       -- withdrawn before a decision
                                                       'CANCELLATION_REQUESTED',      -- approved, employee asks to cancel
                                                       'CANCELLED',                   -- cancellation approved
                                                       'CANCELLED_BY_EMPLOYER')),
                                 total_hours                NUMERIC(8,2) NOT NULL DEFAULT 0,   -- snapshot when submitted
                                 total_days                 NUMERIC(6,2) NOT NULL DEFAULT 0,   -- snapshot when submitted
                                 submitted_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
                                 decided_by                 INT NULL REFERENCES user_account (account_id),
                                 decided_at                 TIMESTAMPTZ NULL,
                                 decision_note              VARCHAR(500),
                                 cancellation_requested_at  TIMESTAMPTZ NULL,
                                 cancel_reason              VARCHAR(500),                      -- employee's reason, or employer's reason
                                 cancel_decided_by          INT NULL REFERENCES user_account (account_id),
                                 cancel_decided_at          TIMESTAMPTZ NULL,
                                 cancel_decision_note       VARCHAR(500),
                                 CONSTRAINT chk_request_dates CHECK (end_date >= start_date),
                                 CONSTRAINT chk_request_cancel CHECK (status <> 'CANCELLATION_REQUESTED' OR cancellation_requested_at IS NOT NULL),
    -- an employee cannot have two live requests on the same day
                                 CONSTRAINT excl_request_overlap EXCLUDE USING gist (
        employee_id WITH =,
        daterange(start_date, end_date, '[]') WITH &&
    ) WHERE (status IN ('SUBMITTED','APPROVED','CANCELLATION_REQUESTED'))
);
CREATE INDEX idx_request_employee ON holiday_request (employee_id, start_date);
CREATE INDEX idx_request_status   ON holiday_request (status);

-- One row per calendar day in the request (weekends/public holidays = 0)
CREATE TABLE holiday_request_day (
                                     request_id      INT NOT NULL REFERENCES holiday_request (request_id) ON DELETE CASCADE,
                                     date            DATE NOT NULL,
                                     hours_deducted  NUMERIC(4,2) NOT NULL DEFAULT 0 CHECK (hours_deducted >= 0),
                                     days_deducted   NUMERIC(3,2) NOT NULL DEFAULT 0 CHECK (days_deducted >= 0),
                                     bucket_id       INT NULL REFERENCES holiday_bucket (bucket_id),   -- NULL when nothing is deducted
                                     PRIMARY KEY (request_id, date)
);

-- Moving unused holiday (max 5 days by law) to the next period. Same flow as a holiday request.
-- Business Esbjerg has TRANSFER_ENABLED = false, but the feature is built for a future policy change.
CREATE TABLE transfer_request (
                                  transfer_id     INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                                  employee_id     INT NOT NULL REFERENCES employee (employee_id),
                                  from_bucket_id  INT NOT NULL REFERENCES holiday_bucket (bucket_id),
                                  to_bucket_id    INT NOT NULL REFERENCES holiday_bucket (bucket_id),
                                  days            NUMERIC(6,2) NOT NULL CHECK (days > 0),
                                  hours           NUMERIC(8,2) NOT NULL CHECK (hours > 0),
                                  status          VARCHAR(20) NOT NULL DEFAULT 'SUBMITTED'
                                      CHECK (status IN ('SUBMITTED','APPROVED','REJECTED','WITHDRAWN')),
                                  submitted_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
                                  decided_by      INT NULL REFERENCES user_account (account_id),
                                  decided_at      TIMESTAMPTZ NULL,
                                  decision_note   VARCHAR(500),
                                  document_ref    VARCHAR(255),
                                  CONSTRAINT chk_transfer_diff CHECK (from_bucket_id <> to_bucket_id)
);

-- A note to payroll that holiday must be paid out. No money here: payroll calculates it.
-- Employees never request payouts; the situation triggers them.
CREATE TABLE payout (
                        payout_id       INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                        employee_id     INT NOT NULL REFERENCES employee (employee_id),
                        bucket_id       INT NOT NULL REFERENCES holiday_bucket (bucket_id),
                        trigger_type    VARCHAR(20) NOT NULL
                            CHECK (trigger_type IN ('TERMINATION','WEEK5_EXPIRY','WEEK6_EXPIRY',
                                                    'OBSTACLE','RETIREMENT','DEATH')),
                        hours           NUMERIC(8,2) NOT NULL CHECK (hours > 0),
                        days            NUMERIC(6,2) NOT NULL CHECK (days >= 0),
                        status          VARCHAR(10) NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','APPROVED','REPORTED')),
                        payroll_month   DATE NULL,
                        created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
                        processed_by    INT NULL REFERENCES user_account (account_id)
);

-- The heart of the system: signed rows, balance = SUM. Rows are never changed or deleted.
CREATE TABLE ledger_entry (
                              entry_id     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                              bucket_id    INT NOT NULL REFERENCES holiday_bucket (bucket_id),
                              entry_type   VARCHAR(20) NOT NULL
                                  CHECK (entry_type IN ('ACCRUAL','USAGE','TRANSFER_OUT','TRANSFER_IN',
                                                        'PAYOUT','ADJUSTMENT','REVERSAL','CONVERSION')),
                              entry_date   DATE NOT NULL,
                              hours        NUMERIC(8,2) NOT NULL,             -- signed
                              days         NUMERIC(6,2) NOT NULL,             -- signed. CONVERSION rows have 0 days
                              request_id   INT NULL REFERENCES holiday_request (request_id),
                              payout_id    INT NULL REFERENCES payout (payout_id),
                              transfer_id  INT NULL REFERENCES transfer_request (transfer_id),
                              schedule_id  INT NULL REFERENCES work_schedule (schedule_id),   -- the new schedule behind a CONVERSION
                              note         VARCHAR(500),
                              created_by   INT NULL REFERENCES user_account (account_id),     -- NULL = system job
                              created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
                              CONSTRAINT chk_ledger_sign CHECK (
                                  (entry_type IN ('ACCRUAL','TRANSFER_IN') AND hours > 0) OR
                                  (entry_type IN ('USAGE','TRANSFER_OUT','PAYOUT') AND hours < 0) OR
                                  (entry_type IN ('ADJUSTMENT','REVERSAL','CONVERSION'))
                                  ),
                              CONSTRAINT chk_ledger_link CHECK (
                                  (entry_type <> 'USAGE'      OR request_id  IS NOT NULL) AND
                                  (entry_type <> 'PAYOUT'     OR payout_id   IS NOT NULL) AND
                                  (entry_type <> 'CONVERSION' OR (schedule_id IS NOT NULL AND days = 0)) AND
                                  (entry_type NOT IN ('TRANSFER_IN','TRANSFER_OUT') OR transfer_id IS NOT NULL)
                                  )
);
CREATE INDEX idx_ledger_bucket ON ledger_entry (bucket_id, entry_date);
-- One accrual row per bucket per month: running the accrual twice can never double-book
CREATE UNIQUE INDEX uq_ledger_accrual_month ON ledger_entry (bucket_id, entry_date) WHERE entry_type = 'ACCRUAL';

-- Accounting rule: mistakes are corrected with a REVERSAL row, never by editing history
CREATE FUNCTION forbid_ledger_change() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'ledger_entry rows are never changed or deleted (% attempted). Add a REVERSAL entry instead.', TG_OP;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_ledger_immutable
    BEFORE UPDATE OR DELETE ON ledger_entry
    FOR EACH ROW EXECUTE FUNCTION forbid_ledger_change();

-- Remaining and earned hours/days per bucket
CREATE VIEW v_bucket_balance AS
SELECT  b.bucket_id,
        b.employee_id,
        b.account_type_id,
        b.earning_start,
        b.earning_end,
        b.usage_start,
        b.usage_end,
        b.status,
        COALESCE(SUM(l.hours), 0)                                              AS remaining_hours,
        COALESCE(SUM(l.days), 0)                                               AS remaining_days,
        COALESCE(SUM(CASE WHEN l.entry_type = 'ACCRUAL' THEN l.hours END), 0)  AS earned_hours,
        COALESCE(SUM(CASE WHEN l.entry_type = 'ACCRUAL' THEN l.days END), 0)   AS earned_days
FROM    holiday_bucket b
            LEFT JOIN ledger_entry l ON l.bucket_id = b.bucket_id
GROUP BY b.bucket_id;

-- ---------------------------------------------------------------------
-- 5. SPECIAL CASES AND AUDIT
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
                                  replacement_hours_granted NUMERIC(8,2) NOT NULL DEFAULT 0,
                                  CONSTRAINT chk_obstacle_dates CHECK (end_date IS NULL OR end_date >= start_date)
);

CREATE TABLE audit_log (
                           log_id      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                           table_name  VARCHAR(60) NOT NULL,
                           row_id      VARCHAR(60) NOT NULL,
                           action      VARCHAR(10) NOT NULL CHECK (action IN ('INSERT','UPDATE','DELETE')),
    old_value   JSONB NULL,
    new_value   JSONB NULL,
    changed_by  INT NULL REFERENCES user_account (account_id),
    changed_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_audit_row ON audit_log (table_name, row_id);

-- ---------------------------------------------------------------------
-- 6. SEED DATA
-- (ids start at 1 because the tables were just recreated above)
-- Users, employees and schedules are created by the app's DevSeeder,
-- because passwords have to be hashed by code.
-- ---------------------------------------------------------------------

INSERT INTO holiday_account_type (code, name, native_unit) VALUES
                                                               ('WEEKS_1_5', 'Weeks 1-5 (Ferieloven, samtidighedsferie)', 'DAYS'),
                                                               ('WEEK_6',    '6th holiday week (KL 05.11, forskudt)',      'HOURS');

INSERT INTO rule_preset (name, description) VALUES
                                                ('Ferieloven (minimum)',                  '5 weeks, nothing else'),
                                                ('Ferieloven + 6. ferieuge (KL 05.11)',   'Model used by Business Esbjerg'),
                                                ('Ferieloven + feriefridage',             'Overenskomst with 5 extra holiday days');

-- Preset 1: minimum
INSERT INTO rule_preset_value (preset_id, param_key, param_value, value_type) VALUES
                                                                                  (1,'W15_DAYS_PER_MONTH','2.08','DECIMAL'),
                                                                                  (1,'W15_EARNING_START','09-01','TEXT'),
                                                                                  (1,'W15_USAGE_END_MONTHS_AFTER_EARNING','16','INT'),
                                                                                  (1,'FULL_TIME_WEEK_HOURS','37','DECIMAL'),
                                                                                  (1,'MAIN_HOLIDAY_DAYS','15','INT'),
                                                                                  (1,'NOTICE_MAIN_MONTHS','3','INT'),
                                                                                  (1,'NOTICE_OTHER_MONTHS','1','INT'),
                                                                                  (1,'W6_ENABLED','false','BOOL'),
                                                                                  (1,'TRANSFER_ENABLED','false','BOOL'),
                                                                                  (1,'TRANSFER_MAX_DAYS','5','INT'),
                                                                                  (1,'DEBT_MAX_DAYS','5','INT'),
                                                                                  (1,'DEBT_CHECK_DATE','08-31','TEXT'),
                                                                                  (1,'PARTIAL_MONTH_DIVISOR','30','DECIMAL'),
                                                                                  (1,'W15_ROUND_WHOLE_DAYS_AT_PERIOD_END','true','BOOL');

-- Preset 2: KL 05.11 model (Business Esbjerg)
INSERT INTO rule_preset_value (preset_id, param_key, param_value, value_type) VALUES
                                                                                  (2,'W15_DAYS_PER_MONTH','2.08','DECIMAL'),
                                                                                  (2,'W15_EARNING_START','09-01','TEXT'),
                                                                                  (2,'W15_USAGE_END_MONTHS_AFTER_EARNING','16','INT'),
                                                                                  (2,'FULL_TIME_WEEK_HOURS','37','DECIMAL'),
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
                                                                                  (2,'TRANSFER_ENABLED','false','BOOL'),
                                                                                  (2,'TRANSFER_MAX_DAYS','5','INT'),
                                                                                  (2,'DEBT_MAX_DAYS','5','INT'),
                                                                                  (2,'DEBT_CHECK_DATE','08-31','TEXT'),
                                                                                  (2,'PARTIAL_MONTH_DIVISOR','30','DECIMAL'),
                                                                                  (2,'W15_ROUND_WHOLE_DAYS_AT_PERIOD_END','true','BOOL');

-- Preset 3: starting point (the system owner fills in the rest)
INSERT INTO rule_preset_value (preset_id, param_key, param_value, value_type) VALUES
                                                                                  (3,'W15_DAYS_PER_MONTH','2.08','DECIMAL'),
                                                                                  (3,'FERIEFRIDAGE_DAYS_PER_YEAR','5','INT');

-- Business Esbjerg's live global rules, copied from preset 2 (in force since the 2020 law)
INSERT INTO rule_parameter (param_key, param_value, value_type, effective_from, source_preset_id)
SELECT param_key, param_value, value_type, DATE '2020-09-01', 2
FROM   rule_preset_value
WHERE  preset_id = 2;

-- Sample public holidays and company days off (the system owner maintains these)
INSERT INTO calendar_day (date, name, day_type) VALUES
                                                    ('2026-12-24', 'Juleaftensdag',   'COMPANY_DAY_OFF'),
                                                    ('2026-12-25', '1. juledag',      'PUBLIC_HOLIDAY'),
                                                    ('2026-12-26', '2. juledag',      'PUBLIC_HOLIDAY'),
                                                    ('2026-12-31', 'Nytaarsaftensdag','COMPANY_DAY_OFF'),
                                                    ('2027-01-01', 'Nytaarsdag',      'PUBLIC_HOLIDAY'),
                                                    ('2027-03-25', 'Skaertorsdag',   'PUBLIC_HOLIDAY'),
                                                    ('2027-03-26', 'Langfredag',     'PUBLIC_HOLIDAY'),
                                                    ('2027-03-28', 'Paaskedag',      'PUBLIC_HOLIDAY'),
                                                    ('2027-03-29', '2. paaskedag',   'PUBLIC_HOLIDAY'),
                                                    ('2027-05-06', 'Kristi himmelfartsdag', 'PUBLIC_HOLIDAY'),
                                                    ('2027-05-16', 'Pinsedag',       'PUBLIC_HOLIDAY'),
                                                    ('2027-05-17', '2. pinsedag',    'PUBLIC_HOLIDAY'),
                                                    ('2027-12-24', 'Juleaftensdag',  'COMPANY_DAY_OFF'),
                                                    ('2027-12-25', '1. juledag',     'PUBLIC_HOLIDAY'),
                                                    ('2027-12-26', '2. juledag',     'PUBLIC_HOLIDAY'),
                                                    ('2027-12-31', 'Nytaarsaftensdag', 'COMPANY_DAY_OFF');

-- ---------------------------------------------------------------------
-- 7. USEFUL QUERIES
-- ---------------------------------------------------------------------
-- Weekly hours per schedule:      SELECT * FROM v_schedule_summary;
-- Balance per bucket:             SELECT * FROM v_bucket_balance WHERE employee_id = 1;
-- Rule value in force on a date:  SELECT DISTINCT ON (param_key) param_key, param_value, effective_from
--                                 FROM rule_parameter WHERE effective_from <= DATE '2026-11-19'
--                                 ORDER BY param_key, effective_from DESC;