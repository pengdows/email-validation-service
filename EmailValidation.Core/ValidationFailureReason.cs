namespace EmailValidation.Core;

/// <summary>
/// Specific reasons why email validation failed.
/// Each reason corresponds to a layer in the validation pipeline.
/// </summary>
public enum ValidationFailureReason
{
    /// <summary>
    /// Invalid format - failed structural split (no @, empty parts, whitespace, control chars)
    /// </summary>
    InvalidFormat,

    /// <summary>
    /// Local part (username) violates RFC rules
    /// (starts/ends with dot, contains consecutive dots, invalid characters)
    /// </summary>
    InvalidLocalPart,

    /// <summary>
    /// Domain does not exist (no DNS A/AAAA records)
    /// </summary>
    DomainDoesNotExist,

    /// <summary>
    /// Domain does not accept mail (no MX records)
    /// </summary>
    DomainDoesNotAcceptMail,

    /// <summary>
    /// Mailbox may not exist (optional SMTP verification failed)
    /// This is never definitive - SMTP can lie
    /// </summary>
    MailboxMayNotExist,

    /// <summary>
    /// Policy violation: local delivery not allowed (e.g., "root" without @domain)
    /// </summary>
    LocalDeliveryNotAllowed
}
