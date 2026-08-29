using System.ComponentModel.DataAnnotations;
using FleetStream.Application.Shared.Results;

namespace FleetStream.Application.Features.FleetAlerts.Acknowledge;

/// <summary>
/// Command that records an operator acknowledgement for a single alert.
/// Returns <see cref="Result"/> to make the success / business-rule failure
/// explicit at the type level rather than via exceptions.
/// </summary>
public sealed record AcknowledgeAlertCommand(
    [property: Required, StringLength(64, MinimumLength = 1)] string AlertId,
    [property: Required, StringLength(64, MinimumLength = 1)] string AcknowledgedBy);