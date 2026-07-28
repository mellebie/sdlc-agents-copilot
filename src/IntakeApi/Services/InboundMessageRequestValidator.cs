using System.Text.RegularExpressions;
using IntakeApi.Contracts;

namespace IntakeApi.Services;

/// <summary>
/// Validates inbound intake requests using explicit field and format checks.
/// </summary>
public sealed class InboundMessageRequestValidator : IInboundMessageRequestValidator
{
    private const int MaximumMessageLength = 1600;
    private static readonly Regex E164PhoneRegex = new(@"^\+[1-9]\d{1,14}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public InboundMessageValidationResult Validate(InboundMessageRequest? request)
    {
        var failures = new List<InboundMessageValidationFailure>();

        if (request is null)
        {
            failures.Add(CreateFailure("request", "INVALID_INPUT", "Request body is required."));
            return new InboundMessageValidationResult(failures);
        }

        ValidateRequiredString(request.EventId, "eventId", "INVALID_INPUT", "Event identifier is required.", failures);
        ValidateRequiredDateTimeOffset(request.ReceivedAtUtc, "receivedAtUtc", "INVALID_INPUT", "Received timestamp is required.", failures);
        ValidatePhoneNumber(request.CustomerPhoneNumber, failures);
        ValidateSourceLdc(request.SourceLdc, failures);
        ValidateSourceApplication(request.SourceApplication, failures);
        ValidateRequiredString(request.CoolTextAccountId, "coolTextAccountId", "INVALID_INPUT", "Cool Text account identifier is required.", failures);
        ValidateMessageText(request.MessageText, failures);

        return new InboundMessageValidationResult(failures);
    }

    private static void ValidateRequiredString(string? value, string fieldName, string code, string message, ICollection<InboundMessageValidationFailure> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(CreateFailure(fieldName, code, message));
        }
    }

    private static void ValidateRequiredDateTimeOffset(DateTimeOffset? value, string fieldName, string code, string message, ICollection<InboundMessageValidationFailure> failures)
    {
        if (value is null)
        {
            failures.Add(CreateFailure(fieldName, code, message));
        }
    }

    private static void ValidatePhoneNumber(string? value, ICollection<InboundMessageValidationFailure> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(CreateFailure("customerPhoneNumber", "INVALID_INPUT", "Customer phone number is required."));
            return;
        }

        var normalizedValue = value.Trim();
        if (!E164PhoneRegex.IsMatch(normalizedValue))
        {
            failures.Add(CreateFailure("customerPhoneNumber", "INVALID_INPUT", "Customer phone number must be in E.164 format."));
        }
    }

    private static void ValidateSourceLdc(SourceLdc? value, ICollection<InboundMessageValidationFailure> failures)
    {
        if (value is null || value == SourceLdc.Unknown)
        {
            failures.Add(CreateFailure("sourceLdc", "INVALID_INPUT", "Source LDC is required."));
        }
    }

    private static void ValidateSourceApplication(SourceApplication? value, ICollection<InboundMessageValidationFailure> failures)
    {
        if (value is null || value == SourceApplication.Unknown)
        {
            failures.Add(CreateFailure("sourceApplication", "INVALID_INPUT", "Source application is required."));
        }
    }

    private static void ValidateMessageText(string? value, ICollection<InboundMessageValidationFailure> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(CreateFailure("messageText", "INVALID_INPUT", "Message text is required."));
            return;
        }

        if (value.Length > MaximumMessageLength)
        {
            failures.Add(CreateFailure("messageText", "INVALID_INPUT", "Message text cannot exceed 1600 characters."));
        }
    }

    private static InboundMessageValidationFailure CreateFailure(string field, string code, string message) =>
        new()
        {
            Field = field,
            Code = code,
            Message = message
        };
}
