namespace TCPA.Core.Services;

/// <summary>
/// Production log event type constants. Use these as the EventType field in structured logs.
/// Phone numbers in production logs must always be hashed via IPhoneNumberHasher.
/// </summary>
public static class LogEventTypes
{
    public const string OptOutReceived = "OPT_OUT_RECEIVED";
    public const string MessageQueued = "MESSAGE_QUEUED";
    public const string MessageSuppressed = "MESSAGE_SUPPRESSED";
    public const string ConfirmationSent = "CONFIRMATION_SENT";
    public const string ConfirmationFailed = "CONFIRMATION_FAILED";
    public const string SlaBreach = "SLA_BREACH";
    public const string AuthFailure = "AUTH_FAILURE";
    public const string AdminReOptIn = "ADMIN_RE_OPT_IN";
    public const string ReportGenerated = "REPORT_GENERATED";
    public const string PotentialViolation = "POTENTIAL_VIOLATION";
}
