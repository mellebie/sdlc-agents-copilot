-- TCPA Compliance API — Application Registry Seed Script
-- TASK-049: Seeds the five in-scope SCG applications into ApplicationRegistrations.
--
-- IMPORTANT — BEFORE RUNNING THIS SCRIPT:
-- 1. Replace all placeholder values (marked [PLACEHOLDER: ...]) with the actual
--    production values obtained from Azure Key Vault / IT Platform team.
-- 2. Confirm callback URLs with each application team.
-- 3. The CoolTextAccountNumber values are sensitive configuration — they MUST be
--    sourced from Azure Key Vault, not hardcoded here.
--    [DECISION-NEEDED from TASK-049: Obtain production Cool Text account IDs from IT/Platform]
--
-- IDEMPOTENCY: This script uses MERGE (upsert) so it is safe to re-run without
-- creating duplicate entries. Re-running will not overwrite an existing entry's
-- callback URL or active flag if they were manually updated.
--
-- POST-SEED VERIFICATION:
--   SELECT ApplicationName, IsActive, CallbackUrl FROM ApplicationRegistrations;
--   Confirm: CCB/My Account IsActive = 0 (false), all others IsActive = 1 (true).
--   Confirm: All CallbackUrl values start with 'https://'

SET NOCOUNT ON;

DECLARE @Now DATETIME2 = SYSUTCDATETIME();
DECLARE @OnboardedDate DATE = CAST(SYSUTCDATETIME() AS DATE);

-- 1. BizTalk (active=true)
--    BizTalk connects via a REST adapter; account number must be confirmed with BizTalk team.
MERGE ApplicationRegistrations AS target
USING (SELECT
    N'[PLACEHOLDER: BizTalk Cool Text Account Number]'   AS CoolTextAccountNumber,
    N'BizTalk'                                            AS ApplicationName,
    N'[PLACEHOLDER: BizTalk Callback URL — must start with https://]' AS CallbackUrl,
    1                                                     AS IsActive,
    @OnboardedDate                                        AS OnboardedDate
) AS source ON target.CoolTextAccountNumber = source.CoolTextAccountNumber
WHEN NOT MATCHED THEN
    INSERT (Id, CoolTextAccountNumber, ApplicationName, CallbackUrl, IsActive, OnboardedDate, CreatedAt, UpdatedAt)
    VALUES (NEWID(), source.CoolTextAccountNumber, source.ApplicationName, source.CallbackUrl,
            source.IsActive, source.OnboardedDate, @Now, @Now);

-- 2. GCMA (active=true)
MERGE ApplicationRegistrations AS target
USING (SELECT
    N'[PLACEHOLDER: GCMA Cool Text Account Number]'      AS CoolTextAccountNumber,
    N'GCMA'                                               AS ApplicationName,
    N'[PLACEHOLDER: GCMA Callback URL — must start with https://]' AS CallbackUrl,
    1                                                     AS IsActive,
    @OnboardedDate                                        AS OnboardedDate
) AS source ON target.CoolTextAccountNumber = source.CoolTextAccountNumber
WHEN NOT MATCHED THEN
    INSERT (Id, CoolTextAccountNumber, ApplicationName, CallbackUrl, IsActive, OnboardedDate, CreatedAt, UpdatedAt)
    VALUES (NEWID(), source.CoolTextAccountNumber, source.ApplicationName, source.CallbackUrl,
            source.IsActive, source.OnboardedDate, @Now, @Now);

-- 3. KMI Active (active=true)
MERGE ApplicationRegistrations AS target
USING (SELECT
    N'[PLACEHOLDER: KMI Active Cool Text Account Number]' AS CoolTextAccountNumber,
    N'KMI Active'                                          AS ApplicationName,
    N'[PLACEHOLDER: KMI Active Callback URL — must start with https://]' AS CallbackUrl,
    1                                                      AS IsActive,
    @OnboardedDate                                         AS OnboardedDate
) AS source ON target.CoolTextAccountNumber = source.CoolTextAccountNumber
WHEN NOT MATCHED THEN
    INSERT (Id, CoolTextAccountNumber, ApplicationName, CallbackUrl, IsActive, OnboardedDate, CreatedAt, UpdatedAt)
    VALUES (NEWID(), source.CoolTextAccountNumber, source.ApplicationName, source.CallbackUrl,
            source.IsActive, source.OnboardedDate, @Now, @Now);

-- 4. ARM/Construction Portal (active=true)
--    Already live — priority integration; callback URL required before go-live.
MERGE ApplicationRegistrations AS target
USING (SELECT
    N'[PLACEHOLDER: ARM/Construction Portal Cool Text Account Number]' AS CoolTextAccountNumber,
    N'ARM/Construction Portal'                                          AS ApplicationName,
    N'[PLACEHOLDER: ARM/Construction Portal Callback URL — must start with https://]' AS CallbackUrl,
    1                                                                   AS IsActive,
    @OnboardedDate                                                      AS OnboardedDate
) AS source ON target.CoolTextAccountNumber = source.CoolTextAccountNumber
WHEN NOT MATCHED THEN
    INSERT (Id, CoolTextAccountNumber, ApplicationName, CallbackUrl, IsActive, OnboardedDate, CreatedAt, UpdatedAt)
    VALUES (NEWID(), source.CoolTextAccountNumber, source.ApplicationName, source.CallbackUrl,
            source.IsActive, source.OnboardedDate, @Now, @Now);

-- 5. CCB/My Account (active=FALSE)
--    Registered but inactive pending CCB go-live verification (ARCH-RISK-006, BR-063).
--    DO NOT set IsActive=1 until the CCB Activation Gate checklist is complete (TASK-052).
MERGE ApplicationRegistrations AS target
USING (SELECT
    N'[PLACEHOLDER: CCB/My Account Cool Text Account Number]' AS CoolTextAccountNumber,
    N'CCB/My Account'                                          AS ApplicationName,
    N'[PLACEHOLDER: CCB/My Account Callback URL — must start with https://]' AS CallbackUrl,
    0                                                          AS IsActive,
    @OnboardedDate                                             AS OnboardedDate
) AS source ON target.CoolTextAccountNumber = source.CoolTextAccountNumber
WHEN NOT MATCHED THEN
    INSERT (Id, CoolTextAccountNumber, ApplicationName, CallbackUrl, IsActive, OnboardedDate, CreatedAt, UpdatedAt)
    VALUES (NEWID(), source.CoolTextAccountNumber, source.ApplicationName, source.CallbackUrl,
            source.IsActive, source.OnboardedDate, @Now, @Now);

-- Verification query — expected output:
-- ApplicationName                 | IsActive
-- BizTalk                         | 1
-- GCMA                            | 1
-- KMI Active                      | 1
-- ARM/Construction Portal         | 1
-- CCB/My Account                  | 0
SELECT ApplicationName, IsActive, CallbackUrl, OnboardedDate
FROM ApplicationRegistrations
WHERE ApplicationName IN ('BizTalk', 'GCMA', 'KMI Active', 'ARM/Construction Portal', 'CCB/My Account')
ORDER BY ApplicationName;
