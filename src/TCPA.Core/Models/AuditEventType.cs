namespace TCPA.Core.Models;

public enum AuditEventType
{
    OptOutWritten,
    OptOutDuplicate,
    ConfirmationDispatched,
    ConfirmationFailed,
    SlaBreach,
    MessageSuppressedQueueTime,
    MessageSuppressedSendTime,
    RaceConditionEdgeCase,
    PotentialViolation,
    ReOptIn,
    ReplyForwarded,
    ReplyForwardFailed,
    MessageDispatched
}
