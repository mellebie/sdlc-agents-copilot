// src/TCPA.Api/Services/OptOut/OptOutStatusService.cs
// TCPA Compliance Engine — Opt-Out Status Write Service Implementation
// Source: TASK-018, TASK-019 | SPEC-004 | STORY-005
// Business Rules: BR-016 through BR-020

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TCPA.Api.Domain;
using TCPA.Api.Infrastructure.Data;

namespace TCPA.Api.Services.OptOut;

/// <summary>
/// Writes and reads the authoritative OPT-OUT status for a cell phone number
/// via the TCPA database.  An opt-out is global across all in-scope SCG
/// applications — there is no per-application scoping (BR-016).
/// </summary>
/// <remarks>
/// All cell phone number values are treated as PII.  Only the last four digits
/// are written to any log output (BR-068 / NFS-007c).
/// </remarks>
public sealed class OptOutStatusService : IOptOutStatusService
{
    private readonly TcpaDbContext _dbContext;
    private readonly ILogger<OptOutStatusService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="OptOutStatusService"/>.
    /// </summary>
    /// <param name="dbContext">EF Core database context for the TCPA store.</param>
    /// <param name="logger">Structured logger.</param>
    public OptOutStatusService(TcpaDbContext dbContext, ILogger<OptOutStatusService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<WriteOptOutResult> WriteOptOutAsync(
        string cellPhoneNumber,
        DateTime eventTimestamp,
        string applicationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cellPhoneNumber))
            throw new ArgumentException("Cell phone number must not be null or whitespace.", nameof(cellPhoneNumber));
        if (string.IsNullOrWhiteSpace(applicationId))
            throw new ArgumentException("Application ID must not be null or whitespace.", nameof(applicationId));

        string maskedNumber = MaskPhoneNumber(cellPhoneNumber);

        _logger.LogInformation(
            "Writing OPT-OUT status for number ****{Masked}, event timestamp {EventTimestamp}, application {ApplicationId}.",
            maskedNumber, eventTimestamp, applicationId);

        try
        {
            CellNumberOptOutRecord? existing = await _dbContext.OptOutRecords
                .FirstOrDefaultAsync(r => r.CellPhoneNumber == cellPhoneNumber, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null && existing.Status == OptOutStatus.OptOut)
            {
                // Idempotent — number is already OPT-OUT; no DB write required (BR-019).
                _logger.LogInformation(
                    "Number ****{Masked} is already OPT-OUT; treating write as idempotent no-op.",
                    maskedNumber);

                return new WriteOptOutResult
                {
                    StatusWriteSuccess = true,
                    PreviousStatus = "OPT_OUT",
                    RecordId = existing.Id,
                };
            }

            if (existing is not null)
            {
                // Existing record with OPT-IN status — update to OPT-OUT.
                existing.Status = OptOutStatus.OptOut;
                existing.LastOptOutTimestamp = eventTimestamp;
                existing.UpdatedAt = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Number ****{Masked} updated from OPT-IN to OPT-OUT (record {RecordId}).",
                    maskedNumber, existing.Id);

                return new WriteOptOutResult
                {
                    StatusWriteSuccess = true,
                    PreviousStatus = "OPT_IN",
                    RecordId = existing.Id,
                };
            }

            // No existing record — create a new OPT-OUT record.
            var newRecord = new CellNumberOptOutRecord
            {
                Id = Guid.NewGuid(),
                CellPhoneNumber = cellPhoneNumber,
                Status = OptOutStatus.OptOut,
                LastOptOutTimestamp = eventTimestamp,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            _dbContext.OptOutRecords.Add(newRecord);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "New OPT-OUT record created for number ****{Masked} (record {RecordId}).",
                maskedNumber, newRecord.Id);

            return new WriteOptOutResult
            {
                StatusWriteSuccess = true,
                PreviousStatus = "OPT_IN",  // Default per ASM-002 / BR-001
                RecordId = newRecord.Id,
            };
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "CRITICAL: Failed to write OPT-OUT status for number ****{Masked}. " +
                "Opt-out status was NOT persisted. An alert should be triggered.",
                maskedNumber);

            return new WriteOptOutResult
            {
                StatusWriteSuccess = false,
                PreviousStatus = string.Empty,
                RecordId = null,
            };
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsOptedOutAsync(
        string cellPhoneNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cellPhoneNumber))
            throw new ArgumentException("Cell phone number must not be null or whitespace.", nameof(cellPhoneNumber));

        string maskedNumber = MaskPhoneNumber(cellPhoneNumber);

        try
        {
            CellNumberOptOutRecord? record = await _dbContext.OptOutRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.CellPhoneNumber == cellPhoneNumber, cancellationToken)
                .ConfigureAwait(false);

            bool isOptedOut = record?.Status == OptOutStatus.OptOut;

            _logger.LogDebug(
                "Opt-out status check for number ****{Masked}: {Status}.",
                maskedNumber, isOptedOut ? "OPT_OUT" : "OPT_IN");

            return isOptedOut;
        }
        catch (Exception ex)
        {
            // Fail-closed: if status cannot be confirmed, the caller must treat as blocked (NFS-005).
            _logger.LogCritical(
                ex,
                "CRITICAL: Database unavailable during opt-out status check for number ****{Masked}. " +
                "Re-throwing so caller can enforce fail-closed (503) behavior.",
                maskedNumber);

            // Re-throw so the caller (OutboundProxyService) can return 503.
            throw;
        }
    }

    /// <summary>
    /// Returns the last four digits of a phone number, prefixed with asterisks,
    /// for safe inclusion in log output (BR-068 / NFS-007c).
    /// </summary>
    /// <param name="phoneNumber">Full E.164 phone number.</param>
    /// <returns>Masked representation, e.g. "****1234".</returns>
    private static string MaskPhoneNumber(string phoneNumber)
    {
        return phoneNumber.Length >= 4
            ? "****" + phoneNumber[^4..]
            : "****";
    }
}
