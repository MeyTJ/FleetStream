using FleetStream.Application.Abstractions;
using FleetStream.Application.Shared.Messaging;
using FleetStream.Application.Shared.Results;
using Microsoft.Extensions.Logging;

namespace FleetStream.Application.Features.FleetAlerts.Acknowledge;

/// <summary>
/// Marks the alert as acknowledged. In Phase 3 the canonical alert store is
/// still owned by Phase 2; this handler is a thin shim that delegates to
/// the <see cref="IAlertService"/>. Once the Streaming Engine exposes an
/// HTTP/Mutating-API for ack the implementation will switch to that client
/// behind the same interface.
/// </summary>
public sealed class AcknowledgeAlertCommandHandler : ICommandHandler<AcknowledgeAlertCommand, Result>
{
    private readonly IAlertService _alerts;
    private readonly ILogger<AcknowledgeAlertCommandHandler> _logger;

    public AcknowledgeAlertCommandHandler(
        IAlertService alerts,
        ILogger<AcknowledgeAlertCommandHandler> logger)
    {
        _alerts = alerts;
        _logger = logger;
    }

    public async Task<Result> Handle(AcknowledgeAlertCommand command, CancellationToken cancellationToken)
    {
        try
        {
            await _alerts.AcknowledgeAlertAsync(
                command.AlertId, command.AcknowledgedBy, cancellationToken);
            _logger.LogInformation("Alert {AlertId} acknowledged by {Subject}",
                command.AlertId, command.AcknowledgedBy);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            // Already acknowledged, unknown id, …
            _logger.LogWarning(ex, "Acknowledge failed for {AlertId}", command.AlertId);
            return Result.Failure("ack.failed", ex.Message);
        }
    }
}