-- TCPA Compliance API — Audit Log Immutability DDL Trigger
-- TASK-064: Creates a database trigger that rejects all UPDATE and DELETE operations
-- on the AuditLogEntries table, enforcing immutability at the database layer (ADR-004).
--
-- This is the DATABASE-LAYER enforcement of audit log immutability.
-- The APPLICATION-LAYER enforcement is the write-only IAuditLogRepository interface
-- (TASK-065), which exposes only Append methods and has no Update/Delete methods.
--
-- Together these two layers implement the defense-in-depth immutability required by
-- the regulatory audit retention requirements (SPEC-008, SPEC-009, SPEC-010).
--
-- AFTER APPLYING: Verify the trigger is active by running the test block below.
-- The test UPDATE and DELETE must both produce errors.

-- Apply the immutability trigger
IF OBJECT_ID('trg_AuditLogEntries_Immutability', 'TR') IS NOT NULL
    DROP TRIGGER trg_AuditLogEntries_Immutability;
GO

CREATE TRIGGER trg_AuditLogEntries_Immutability
ON AuditLogEntries
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR(
        'AuditLogEntries records are immutable. UPDATE and DELETE operations are not permitted. ' +
        'TCPA compliance requires an append-only audit log (ADR-004).',
        16,  -- Severity 16 = user error, will cause transaction rollback
        1    -- State
    );
    ROLLBACK TRANSACTION;
END;
GO

-- Verification: The following block should produce errors (not data changes).
-- Run this block in a transaction to avoid any accidental side effects.
-- Expected: Both ROLLBACK operations should succeed (no rows changed).
/*
BEGIN TRANSACTION;
    -- Test 1: UPDATE should fail
    BEGIN TRY
        UPDATE TOP (1) AuditLogEntries SET SystemResponse = 'TAMPERED_VALUE';
        PRINT 'ERROR: UPDATE should have been rejected by trigger';
        ROLLBACK;
    END TRY
    BEGIN CATCH
        PRINT 'PASS: UPDATE correctly rejected by trigger: ' + ERROR_MESSAGE();
        ROLLBACK;
    END CATCH;

    -- Test 2: DELETE should fail
    BEGIN TRY
        BEGIN TRANSACTION;
        DELETE TOP (1) FROM AuditLogEntries;
        PRINT 'ERROR: DELETE should have been rejected by trigger';
        ROLLBACK;
    END TRY
    BEGIN CATCH
        PRINT 'PASS: DELETE correctly rejected by trigger: ' + ERROR_MESSAGE();
        IF @@TRANCOUNT > 0 ROLLBACK;
    END CATCH;
*/
