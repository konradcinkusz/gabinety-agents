using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Gabinety.Contracts.Common;

namespace Gabinety.Contracts.Booking;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccessStatus
{
    PendingPayment, Confirmed, CancelledByClient, LateCancelledByClient, CancelledByPractitioner, Expired,
    Completed, ClientNoShow, Disputed, Resolved,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentStatus { Created, Succeeded, Failed, Refunded, PartiallyRefunded }

[JsonConverter(typeof(Gabinety.Contracts.Common.SnakeCaseLowerEnumConverter))]
public enum DisputeKind { NotAsDescribed, PractitionerNoShow, Other }

[JsonConverter(typeof(Gabinety.Contracts.Common.SnakeCaseLowerEnumConverter))]
public enum Resolution { Release, RefundFull, RefundPartial }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LedgerKind { ClientCharge, Refund, PlatformFee, PractitionerPayout, PayoutReversal }


public sealed record BriefDto([property: Required, StringLength(2000, MinimumLength = 20)] string Text, string? ProblemSlug, bool AiAssisted);
/// <summary><c>StartUtc</c> is accepted for older clients and ignored: a package is valid from the moment of purchase. Last, because an optional parameter cannot precede a required one.</summary>
public sealed record CreateAccessRequest([property: JsonPropertyName("offeringId")] Guid PackageId, BriefDto Brief, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantNullableConverter))] DateTimeOffset? StartUtc = null);
public sealed record CreateAccessResponse([property: JsonPropertyName("bookingId")] Guid AccessId, AccessStatus Status, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset HoldExpiresAt, string CheckoutUrl);

public sealed record CancelRequest([property: StringLength(500)] string? Reason = null);
public sealed record DisputeRequest(DisputeKind Kind, [property: Required, StringLength(1000, MinimumLength = 10)] string Text);
public sealed record ResolveRequest(Resolution Resolution, long? RefundMinor, [property: Required, StringLength(500, MinimumLength = 10)] string Note);
public sealed record SimulatePaymentRequest([property: JsonPropertyName("bookingId")] Guid AccessId, string Outcome);

/// <summary>What cancelling would do for the viewer right now (CONTRACTS 4.4, 9.2). <c>partial</c> is part of the vocabulary; today's rules give only the other two.</summary>
[JsonConverter(typeof(Gabinety.Contracts.Common.SnakeCaseLowerEnumConverter))]
public enum CancelOutcomeKind { FullRefund, Partial, NoRefund }

/// <summary><c>cancelOutcome</c> of a <see cref="AccessView"/>: present only when <c>canCancel</c> is true; <c>refund</c> is the amount returned to the client, in grosz.</summary>
public sealed record CancelOutcomeDto(CancelOutcomeKind Outcome, MoneyDto Refund);

public sealed record AgentRef(Guid Id, string DisplayName, string Slug);
public sealed record ClientRef(string DisplayName);
public sealed record PaymentView(PaymentStatus Status, MoneyDto Refunded);

public sealed record AccessView(Guid Id, AccessStatus Status, string? StatusReason, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset StartUtc, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset EndUtc,
    int DurationMinutes, [property: JsonPropertyName("offeringTitle")] string PackageTitle, MoneyDto Price, [property: JsonPropertyName("practitioner")] AgentRef Agent, ClientRef? Client, BriefDto? Brief,
    string? MeetingUrl, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantNullableConverter))] DateTimeOffset? HoldExpiresAt, PaymentView Payment, MoneyDto? Fee, MoneyDto? Payout,
    bool CanCancel, bool CanDispute, bool CanReportNoShow, bool CanReview, CancelOutcomeDto? CancelOutcome = null,
    string Kind = "Tasks", long UnitsTotal = 0, long UnitsUsed = 0);

public sealed record ReviewabilityDto(bool Reviewable, string ClientUserId, [property: JsonPropertyName("practitionerId")] Guid AgentId, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantNullableConverter))] DateTimeOffset? CompletedAt);

/// <summary>One row of <c>GET /internal/v1/analytics/pair-counts</c> (T-10.03): a client-practitioner pair and its paid bookings. <c>PairKey</c> is the fee hook's key. Ids and a count only.</summary>
public sealed record PairCountDto(string PairKey, string ClientUserId, [property: JsonPropertyName("practitionerId")] Guid AgentId, [property: JsonPropertyName("bookings")] int Accesses);

/// <summary>Body of <c>POST /api/v1/me/api-keys</c> (docs/agentic/PLAN.md B-01). The name is user free text: 1 to 60 characters.</summary>
public sealed record CreateApiKeyRequest(string? Name);

/// <summary>An API key as listed: never the key itself, only its 8-character <c>Prefix</c> (the characters after <c>gab_</c>).</summary>
public sealed record ApiKeyDto(Guid Id, string Name, string Prefix, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset CreatedAt,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantNullableConverter))] DateTimeOffset? LastUsedAt);

/// <summary>Answer of <c>POST /api/v1/me/api-keys</c>: the only time the plain <c>Key</c> is ever returned.</summary>
public sealed record ApiKeyCreatedDto(Guid Id, string Name, string Prefix, string Key, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset CreatedAt);
