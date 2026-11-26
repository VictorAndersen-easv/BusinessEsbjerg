-- Run this ONCE on your existing database, to get ready for real holiday requests.
-- (A fresh database gets the calendar days from SQL.sql.)

-- 1. Remove the old SAMPLE requests that the seeder created. Real requests are made through the app now,
--    and the samples have no ledger rows, so they would only get in the way. Only requests that have
--    nothing booked against them are removed.
DELETE FROM holiday_request r
WHERE NOT EXISTS (SELECT 1 FROM ledger_entry l WHERE l.request_id = r.request_id)
  AND NOT EXISTS (SELECT 1 FROM holiday_request_day d WHERE d.request_id = r.request_id);

-- 2. Public holidays and company days off for 2027 (written from memory:
--    check them against an official calendar and adjust the company days to your own).
INSERT INTO calendar_day (date, name, day_type) VALUES
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
                                                    ('2027-12-31', 'Nytaarsaftensdag', 'COMPANY_DAY_OFF')
    ON CONFLICT DO NOTHING;