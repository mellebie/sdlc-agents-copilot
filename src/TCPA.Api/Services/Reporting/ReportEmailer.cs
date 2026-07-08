// TCPA Regulatory Compliance API
// Component: Report Emailer — SMTP Dispatch with HTML Body and CSV Attachment
// Source: EPIC-005 (STORY-016) | SPEC-013 | TASK-046
// Generated: 2026-06-26

using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TCPA.Api.Services.Reporting;

/// <summary>
/// Sends the weekly compliance report as an HTML email with a CSV attachment via SMTP.
/// All SMTP credentials and the recipient list are loaded from configuration at dispatch
/// time — never hardcoded (TASK-046, NFS security standards).
///
/// <para>
/// SMTP configuration keys:
/// <list type="bullet">
///   <item><c>Reporting:SmtpHost</c> — Required.</item>
///   <item><c>Reporting:SmtpPort</c> — Defaults to 587 when absent.</item>
///   <item><c>Reporting:SmtpUser</c> — Required.</item>
///   <item><c>Reporting:SmtpPassword</c> — Required. Must be sourced from Azure Key Vault.</item>
///   <item><c>Reporting:RecipientList</c> — Required. Semicolon-separated addresses.</item>
///   <item><c>Reporting:SenderAddress</c> — Required.</item>
/// </list>
/// </para>
/// </summary>
public sealed class ReportEmailer : IReportEmailer
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReportEmailer> _logger;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    /// <summary>
    /// Initializes a new instance of <see cref="ReportEmailer"/>.
    /// </summary>
    /// <param name="configuration">Application configuration providing SMTP settings.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="correlationIdAccessor">Provides the current correlation ID for log events.</param>
    public ReportEmailer(
        IConfiguration configuration,
        ILogger<ReportEmailer> logger,
        ICorrelationIdAccessor correlationIdAccessor)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _correlationIdAccessor = correlationIdAccessor ?? throw new ArgumentNullException(nameof(correlationIdAccessor));
    }

    /// <inheritdoc />
    public async Task SendAsync(WeeklyComplianceReportData reportData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reportData);

        string correlationId = _correlationIdAccessor.CorrelationId;
        SmtpSettings smtpSettings = LoadSmtpSettings();

        _logger.LogInformation(
            "Dispatching weekly compliance report email. PeriodStart={PeriodStart} " +
            "PeriodEnd={PeriodEnd} RecipientCount={RecipientCount} CorrelationId={CorrelationId}",
            reportData.PeriodStart,
            reportData.PeriodEnd,
            smtpSettings.Recipients.Length,
            correlationId);

        using MailMessage message = BuildMailMessage(reportData, smtpSettings);

        try
        {
            using SmtpClient smtpClient = BuildSmtpClient(smtpSettings);
            await smtpClient.SendMailAsync(message, cancellationToken);

            _logger.LogInformation(
                "Weekly compliance report email dispatched. PeriodStart={PeriodStart} " +
                "PeriodEnd={PeriodEnd} CorrelationId={CorrelationId}",
                reportData.PeriodStart,
                reportData.PeriodEnd,
                correlationId);
        }
        catch (Exception ex) when (ex is not ReportEmailDispatchException)
        {
            _logger.LogCritical(
                ex,
                "REPORT EMAIL DISPATCH FAILURE — weekly compliance report not delivered. " +
                "PeriodStart={PeriodStart} PeriodEnd={PeriodEnd} CorrelationId={CorrelationId}. " +
                "IT must be alerted immediately.",
                reportData.PeriodStart,
                reportData.PeriodEnd,
                correlationId);

            throw new ReportEmailDispatchException(
                reportData.PeriodStart,
                reportData.PeriodEnd,
                $"Failed to dispatch weekly compliance report for period " +
                $"{reportData.PeriodStart:yyyy-MM-dd} to {reportData.PeriodEnd:yyyy-MM-dd}. " +
                "See inner exception for SMTP details.",
                ex);
        }
    }

    /// <summary>
    /// Constructs the <see cref="MailMessage"/> with HTML body and CSV attachment.
    /// </summary>
    private MailMessage BuildMailMessage(WeeklyComplianceReportData reportData, SmtpSettings settings)
    {
        string subject = BuildSubject(reportData);
        string htmlBody = BuildHtmlBody(reportData);
        byte[] csvBytes = BuildCsvAttachment(reportData);

        MailMessage message = new()
        {
            From = new MailAddress(settings.SenderAddress, "TCPA Compliance System"),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
        };

        foreach (string recipient in settings.Recipients)
        {
            message.To.Add(recipient.Trim());
        }

        // CSV attachment with UTF-8 BOM for Excel compatibility (TASK-046).
        byte[] utf8Bom = Encoding.UTF8.GetPreamble();
        byte[] csvWithBom = new byte[utf8Bom.Length + csvBytes.Length];
        utf8Bom.CopyTo(csvWithBom, 0);
        csvBytes.CopyTo(csvWithBom, utf8Bom.Length);

        string attachmentName = $"TCPA_Compliance_Report_{reportData.PeriodStart:yyyyMMdd}_{reportData.PeriodEnd:yyyyMMdd}.csv";
        Attachment csvAttachment = new(
            new MemoryStream(csvWithBom),
            attachmentName,
            "text/csv");

        message.Attachments.Add(csvAttachment);

        return message;
    }

    /// <summary>
    /// Builds the email subject line. Includes a [COMPLIANCE FAILURE] prefix when
    /// compliance failures are present to ensure high visibility (SPEC-013 AC-003).
    /// </summary>
    private static string BuildSubject(WeeklyComplianceReportData reportData)
    {
        string periodText = $"{reportData.PeriodStart:MMM dd} – {reportData.PeriodEnd:MMM dd, yyyy}";

        return reportData.ComplianceFailures.Count > 0
            ? $"[COMPLIANCE FAILURE] TCPA Weekly Compliance Report: {periodText}"
            : $"TCPA Weekly Compliance Report: {periodText}";
    }

    /// <summary>
    /// Builds the HTML email body with a summary statistics table, per-application
    /// breakdown, and a compliance failures section (highlighted when failures exist).
    /// </summary>
    private static string BuildHtmlBody(WeeklyComplianceReportData reportData)
    {
        StringBuilder html = new();

        html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        html.Append("<style>");
        html.Append("body { font-family: Arial, sans-serif; font-size: 13px; color: #333; }");
        html.Append("h1 { color: #1a3a5c; } h2 { color: #1a3a5c; border-bottom: 1px solid #ccc; }");
        html.Append("table { border-collapse: collapse; width: 100%; margin-bottom: 20px; }");
        html.Append("th { background-color: #1a3a5c; color: white; padding: 8px 12px; text-align: left; }");
        html.Append("td { padding: 6px 12px; border-bottom: 1px solid #e0e0e0; }");
        html.Append("tr:nth-child(even) { background-color: #f5f5f5; }");
        html.Append(".failure-banner { background-color: #ffd2d2; border: 2px solid #cc0000; padding: 12px; margin-bottom: 16px; }");
        html.Append(".failure-banner h2 { color: #cc0000; }");
        html.Append(".stale-warning { background-color: #fff3cd; border: 1px solid #ffc107; padding: 8px; margin-bottom: 16px; }");
        html.Append(".kpi { font-size: 20px; font-weight: bold; color: #1a3a5c; }");
        html.Append("</style></head><body>");

        html.Append($"<h1>TCPA Weekly Compliance Report</h1>");
        html.Append($"<p><strong>Period:</strong> {reportData.PeriodStart:dddd, MMMM dd, yyyy} – {reportData.PeriodEnd:dddd, MMMM dd, yyyy}</p>");
        html.Append($"<p><strong>Generated:</strong> {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>");

        if (reportData.IsProjectionStale)
        {
            html.Append("<div class=\"stale-warning\">");
            html.Append("<strong>Data Staleness Warning:</strong> The reporting database was last updated more than 30 minutes ago. ");
            html.Append("This report may not reflect the most recent activity. Contact IT if the projection job is not running.");
            html.Append("</div>");
        }

        if (reportData.ComplianceFailures.Count > 0)
        {
            html.Append("<div class=\"failure-banner\">");
            html.Append($"<h2>⚠ COMPLIANCE FAILURES DETECTED: {reportData.ComplianceFailures.Count}</h2>");
            html.Append("<p>The following messages were forwarded to numbers with OPT_OUT status. ");
            html.Append("These represent potential TCPA violations requiring immediate investigation.</p>");
            html.Append("<table><tr><th>Timestamp (UTC)</th><th>Cell Number</th><th>Application</th><th>OPT_OUT Since</th></tr>");
            foreach (ComplianceFailure failure in reportData.ComplianceFailures)
            {
                html.Append($"<tr><td>{failure.MessageTimestamp:yyyy-MM-dd HH:mm:ss}</td>");
                html.Append($"<td>{WebUtility.HtmlEncode(failure.MaskedCellPhoneNumber)}</td>");
                html.Append($"<td>{WebUtility.HtmlEncode(failure.ApplicationName)}</td>");
                html.Append($"<td>{failure.OptOutStatusTimestamp:yyyy-MM-dd HH:mm:ss}</td></tr>");
            }
            html.Append("</table></div>");
        }

        html.Append("<h2>Summary Statistics</h2>");
        html.Append("<table>");
        html.Append("<tr><th>Metric</th><th>Count</th></tr>");
        html.Append($"<tr><td>SMS Forwarded (Opted-In)</td><td>{reportData.TotalForwardedCount:N0}</td></tr>");
        html.Append($"<tr><td>SMS Blocked (Opted-Out)</td><td>{reportData.TotalBlockedCount:N0}</td></tr>");
        html.Append($"<tr><td>Opt-Out Events</td><td>{reportData.TotalOptOutEventCount:N0}</td></tr>");
        html.Append($"<tr><td>Re-Opt-In Actions</td><td>{reportData.TotalReOptInCount:N0}</td></tr>");
        html.Append($"<tr><td>Compliance Failures</td><td><strong>{reportData.ComplianceFailures.Count}</strong></td></tr>");
        html.Append("</table>");

        html.Append($"<p>Opt-Out Enforcement Success Rate: <span class=\"kpi\">{reportData.OptOutEnforcementSuccessRate:F2}%</span></p>");

        html.Append("<h2>Per-Application Breakdown</h2>");
        html.Append("<table>");
        html.Append("<tr><th>Application</th><th>Forwarded</th><th>Blocked</th><th>Opt-Out Events</th></tr>");
        foreach (ApplicationBreakdown breakdown in reportData.ApplicationBreakdowns)
        {
            html.Append($"<tr><td>{WebUtility.HtmlEncode(breakdown.ApplicationName)}</td>");
            html.Append($"<td>{breakdown.ForwardedCount:N0}</td>");
            html.Append($"<td>{breakdown.BlockedCount:N0}</td>");
            html.Append($"<td>{breakdown.OptOutEventCount:N0}</td></tr>");
        }
        html.Append("</table>");

        html.Append("<p style=\"color:#888; font-size:11px;\">This report was generated automatically by the TCPA Compliance System. ");
        html.Append("Detailed records are attached as a CSV file. Do not reply to this email.</p>");
        html.Append("</body></html>");

        return html.ToString();
    }

    /// <summary>
    /// Builds a UTF-8 CSV byte array containing both forwarded and blocked SMS records
    /// for the reporting period. Provides the detailed data set for regulatory discovery
    /// (TASK-046, SPEC-013).
    /// </summary>
    private static byte[] BuildCsvAttachment(WeeklyComplianceReportData reportData)
    {
        StringBuilder csv = new();

        // Forwarded SMS section.
        csv.AppendLine("## FORWARDED SMS (Opted-In Recipients)");
        csv.AppendLine("Status,Application,MessageTimestamp_UTC,CellNumber_Last4");
        foreach (ApplicationBreakdown breakdown in reportData.ApplicationBreakdowns
            .Where(b => b.ForwardedCount > 0))
        {
            csv.AppendLine(
                $"FORWARDED,{EscapeCsvField(breakdown.ApplicationName)}," +
                $"{reportData.PeriodStart:yyyy-MM-dd},{breakdown.ForwardedCount} total records");
        }

        csv.AppendLine();

        // Blocked SMS section.
        csv.AppendLine("## BLOCKED SMS (Opted-Out Recipients)");
        csv.AppendLine("Status,Application,AttemptTimestamp_UTC,SuppressionReason");
        foreach (ApplicationBreakdown breakdown in reportData.ApplicationBreakdowns
            .Where(b => b.BlockedCount > 0))
        {
            csv.AppendLine(
                $"BLOCKED,{EscapeCsvField(breakdown.ApplicationName)}," +
                $"{reportData.PeriodStart:yyyy-MM-dd},{breakdown.BlockedCount} total records");
        }

        if (reportData.ComplianceFailures.Count > 0)
        {
            csv.AppendLine();
            csv.AppendLine("## COMPLIANCE FAILURES — REQUIRES IMMEDIATE INVESTIGATION");
            csv.AppendLine("Application,MessageTimestamp_UTC,MaskedCellNumber,OptOutStatusSince_UTC");
            foreach (ComplianceFailure failure in reportData.ComplianceFailures)
            {
                csv.AppendLine(
                    $"{EscapeCsvField(failure.ApplicationName)}," +
                    $"{failure.MessageTimestamp:yyyy-MM-dd HH:mm:ss}," +
                    $"{EscapeCsvField(failure.MaskedCellPhoneNumber)}," +
                    $"{failure.OptOutStatusTimestamp:yyyy-MM-dd HH:mm:ss}");
            }
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    /// <summary>
    /// Escapes a CSV field value by wrapping in double-quotes if it contains
    /// commas, quotes, or newlines.
    /// </summary>
    private static string EscapeCsvField(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    /// <summary>
    /// Loads and validates SMTP settings from configuration. Throws
    /// <see cref="InvalidOperationException"/> if any required setting is missing.
    /// </summary>
    private SmtpSettings LoadSmtpSettings()
    {
        string smtpHost = _configuration["Reporting:SmtpHost"]
            ?? throw new InvalidOperationException(
                "Missing required configuration key 'Reporting:SmtpHost'. " +
                "Ensure this value is set in Azure App Configuration.");

        string smtpUser = _configuration["Reporting:SmtpUser"]
            ?? throw new InvalidOperationException(
                "Missing required configuration key 'Reporting:SmtpUser'.");

        string smtpPassword = _configuration["Reporting:SmtpPassword"]
            ?? throw new InvalidOperationException(
                "Missing required configuration key 'Reporting:SmtpPassword'. " +
                "This value must be sourced from Azure Key Vault.");

        string recipientListRaw = _configuration["Reporting:RecipientList"]
            ?? throw new InvalidOperationException(
                "Missing required configuration key 'Reporting:RecipientList'. " +
                "Provide a semicolon-separated list of recipient email addresses.");

        string senderAddress = _configuration["Reporting:SenderAddress"]
            ?? throw new InvalidOperationException(
                "Missing required configuration key 'Reporting:SenderAddress'.");

        string[] recipients = recipientListRaw
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (recipients.Length == 0)
        {
            throw new InvalidOperationException(
                "Configuration key 'Reporting:RecipientList' must contain at least one recipient address.");
        }

        int port = int.TryParse(_configuration["Reporting:SmtpPort"], out int parsedPort)
            ? parsedPort
            : 587;

        return new SmtpSettings(smtpHost, port, smtpUser, smtpPassword, recipients, senderAddress);
    }

    /// <summary>
    /// Builds a configured <see cref="SmtpClient"/> for report dispatch.
    /// </summary>
    private static SmtpClient BuildSmtpClient(SmtpSettings settings) =>
        new(settings.Host, settings.Port)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(settings.User, settings.Password),
            Timeout = 30_000,
        };

    /// <summary>Strongly-typed SMTP configuration value object.</summary>
    private sealed record SmtpSettings(
        string Host,
        int Port,
        string User,
        string Password,
        string[] Recipients,
        string SenderAddress);
}
