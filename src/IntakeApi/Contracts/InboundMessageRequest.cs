namespace IntakeApi.Contracts;

/// <summary>
/// Represents the canonical inbound message intake request.
/// </summary>
public sealed class InboundMessageRequest
{
    /// <summary>
    /// Gets or sets the external event identifier.
    /// </summary>
    public string? EventId { get; init; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the message was received.
    /// </summary>
    public DateTimeOffset? ReceivedAtUtc { get; init; }

    /// <summary>
    /// Gets or sets the customer's E.164 phone number.
    /// </summary>
    public string? CustomerPhoneNumber { get; init; }

    /// <summary>
    /// Gets or sets the source LDC.
    /// </summary>
    public SourceLdc? SourceLdc { get; init; }

    /// <summary>
    /// Gets or sets the source application.
    /// </summary>
    public SourceApplication? SourceApplication { get; init; }

    /// <summary>
    /// Gets or sets the Cool Text account identifier.
    /// </summary>
    public string? CoolTextAccountId { get; init; }

    /// <summary>
    /// Gets or sets the original message text.
    /// </summary>
    public string? MessageText { get; init; }
}

/// <summary>
/// Supported source LDCs for intake routing.
/// </summary>
public enum SourceLdc
{
    /// <summary>Unspecified or invalid source.</summary>
    Unknown = 0,

    /// <summary>VNG source.</summary>
    Vng,

    /// <summary>CGC source.</summary>
    Cgc,

    /// <summary>Nicor source.</summary>
    Nicor,

    /// <summary>AGL source.</summary>
    Agl
}

/// <summary>
/// Supported source applications for intake routing.
/// </summary>
public enum SourceApplication
{
    /// <summary>Unspecified or invalid source.</summary>
    Unknown = 0,

    /// <summary>BizTalk application.</summary>
    BizTalk,

    /// <summary>GCMA application.</summary>
    Gcma,

    /// <summary>KMI application.</summary>
    Kmi,

    /// <summary>ARM application.</summary>
    Arm,

    /// <summary>CCB application.</summary>
    Ccb,

    /// <summary>My Account application.</summary>
    MyAccount
}
