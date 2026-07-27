namespace Hsm.Domain.Coms;

/// <summary>
/// An email send batch (frozen email_batch): the aggregate root over its
/// per-recipient rows. Recipients are reachable through the aggregate; a
/// recipient removed from <see cref="Recipients"/> is deleted, not orphaned
/// (the EF Core change-tracking behavior the stack was chosen for, proven by
/// integration test against real PostgreSQL).
/// </summary>
public class EmailBatch
{
    public Guid Id { get; set; }

    public Guid? TemplateId { get; set; }

    public string? FromEmail { get; set; }

    public string? FromName { get; set; }

    /// <summary>The render data (jsonb) the batch was created with.</summary>
    public string DataJson { get; set; } = "{}";

    public List<string>? DocumentIds { get; set; }

    public string? JobId { get; set; }

    public string? ProviderMessageId { get; set; }

    /// <summary>One of <see cref="EmailBatchStatus"/>.</summary>
    public string OverallStatus { get; set; } = EmailBatchStatus.Pending;

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<EmailRecipient> Recipients { get; set; } = [];
}

/// <summary>One recipient of a batch (frozen email_recipient).</summary>
public class EmailRecipient
{
    public Guid Id { get; set; }

    public Guid BatchId { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    public string? MessageId { get; set; }

    /// <summary>One of <see cref="EmailRecipientStatus"/>.</summary>
    public string Status { get; set; } = EmailRecipientStatus.Pending;

    public DateTimeOffset? SentAt { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>
/// A suppressed address (frozen email_suppression): unique per email; first
/// suppression wins, the reason is never updated.
/// </summary>
public class EmailSuppression
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    /// <summary>One of <see cref="EmailSuppressionReasons"/>.</summary>
    public string Reason { get; set; } = string.Empty;

    public Guid? SourceWebhookEventId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// One received provider webhook event, normalized (frozen
/// email_webhook_event). <see cref="ProcessedAt"/> is the processing
/// idempotency marker.
/// </summary>
public class EmailWebhookEvent
{
    public Guid Id { get; set; }

    public string Provider { get; set; } = string.Empty;

    /// <summary>One of <see cref="EmailWebhookEventTypes"/>.</summary>
    public string EventType { get; set; } = string.Empty;

    public string RawPayloadJson { get; set; } = "{}";

    public string RecipientEmail { get; set; } = string.Empty;

    public string? MessageId { get; set; }

    public Guid? RecipientId { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Frozen EmailBatchStatusEnum values.</summary>
public static class EmailBatchStatus
{
    public const string Pending = "PENDING";
    public const string Processing = "PROCESSING";
    public const string Sent = "SENT";
    public const string Partial = "PARTIAL";
    public const string Failed = "FAILED";

    public static readonly IReadOnlyList<string> All = [Pending, Processing, Sent, Partial, Failed];
}

/// <summary>Frozen EmailRecipientStatusEnum values.</summary>
public static class EmailRecipientStatus
{
    public const string Pending = "PENDING";
    public const string Sent = "SENT";
    public const string Failed = "FAILED";
    public const string Suppressed = "SUPPRESSED";
    public const string Delivered = "DELIVERED";
    public const string BouncedHard = "BOUNCED_HARD";
    public const string BouncedSoft = "BOUNCED_SOFT";
    public const string Spam = "SPAM";

    public static readonly IReadOnlyList<string> All =
        [Pending, Sent, Failed, Suppressed, Delivered, BouncedHard, BouncedSoft, Spam];
}

/// <summary>Frozen EmailWebhookEventTypeEnum values.</summary>
public static class EmailWebhookEventTypes
{
    public const string Delivered = "DELIVERED";
    public const string BouncedHard = "BOUNCED_HARD";
    public const string BouncedSoft = "BOUNCED_SOFT";
    public const string Spam = "SPAM";
    public const string Deferred = "DEFERRED";
    public const string Open = "OPEN";
    public const string Click = "CLICK";
    public const string Unsubscribed = "UNSUBSCRIBED";
    public const string Unknown = "UNKNOWN";
}

/// <summary>Frozen EmailSuppressionReasonEnum values.</summary>
public static class EmailSuppressionReasons
{
    public const string HardBounce = "HARD_BOUNCE";
    public const string SpamComplaint = "SPAM_COMPLAINT";
    public const string Manual = "MANUAL";
}
