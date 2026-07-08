// TCPA Regulatory Compliance API
// Component: Report Emailer Interface
// Source: EPIC-005 (STORY-016) | SPEC-013 | TASK-046
// Generated: 2026-06-26

namespace TCPA.Api.Services.Reporting;

/// <summary>
/// Defines the contract for dispatching weekly compliance report emails to the
/// Compliance Officer distribution list (SPEC-013, TASK-046).
///
/// <para>
/// Configuration keys consumed by implementations:
/// <list type="bullet">
///   <item><c>Reporting:SmtpHost</c> — SMTP relay hostname.</item>
///   <item><c>Reporting:SmtpPort</c> — SMTP port (defaults to 587 if absent).</item>
///   <item><c>Reporting:SmtpUser</c> — SMTP authentication username.</item>
///   <item><c>Reporting:SmtpPassword</c> — SMTP authentication password (from Azure Key Vault).</item>
///   <item><c>Reporting:RecipientList</c> — Semicolon-separated recipient email addresses.</item>
///   <item><c>Reporting:SenderAddress</c> — From address for outbound report emails.</item>
/// </list>
/// </para>
/// </summary>
public interface IReportEmailer
{
    /// <summary>
    /// Sends the weekly compliance report as an HTML email with a CSV attachment to the
    /// Compliance Officer distribution list.
    /// </summary>
    /// <param name="reportData">
    /// The aggregated report data to render. Must not be null.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the email is dispatched to the SMTP relay.</returns>
    /// <exception cref="ReportEmailDispatchException">
    /// Thrown when the email cannot be delivered to the SMTP relay. The caller
    /// (Azure Function job) must log this as a critical alert and retry according
    /// to its configured retry policy (TASK-046, TASK-047).
    /// </exception>
    Task SendAsync(WeeklyComplianceReportData reportData, CancellationToken cancellationToken = default);
}
