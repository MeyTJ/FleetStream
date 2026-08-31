using Asp.Versioning;
using FleetStream.Application.Features.FleetAlerts.Acknowledge;
using FleetStream.Application.Features.FleetAlerts.List;
using FleetStream.Application.Features.FleetSummary.Get;
using FleetStream.Application.Features.FleetTrucks.GetState;
using FleetStream.Application.Features.FleetTrucks.GetTelemetry;
using FleetStream.Application.Features.FleetTrucks.GetTruck;
using FleetStream.Application.Features.FleetTrucks.List;
using FleetStream.Application.Shared.Messaging;
using FleetStream.Application.Shared.Pagination;
using FleetStream.Application.Shared.Results;
using FleetStream.Core.Domain.Entities;
using FleetStream.Infrastructure.Metrics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FleetStream.Presentation.Controllers;

[ApiController]
[Authorize]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/fleet")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
public sealed class FleetController : ControllerBase
{
    private readonly IQueryHandler<GetFleetSummaryQuery, FleetSummaryDto> _getSummary;
    private readonly IQueryHandler<GetTruckStatesQuery, Page<TruckState>> _getTruckStates;
    private readonly IQueryHandler<GetTruckStateQuery, TruckState?> _getTruckState;
    private readonly IQueryHandler<GetTruckQuery, Truck?> _getTruck;
    private readonly IQueryHandler<ListAlertsQuery, Page<Alert>> _listAlerts;
    private readonly IQueryHandler<GetTruckTelemetryQuery, IReadOnlyList<TruckTelemetry>> _getTelemetry;
    private readonly ICommandHandler<AcknowledgeAlertCommand, Result> _acknowledgeAlert;
    private readonly ILogger<FleetController> _logger;

    public FleetController(
        IQueryHandler<GetFleetSummaryQuery, FleetSummaryDto> getSummary,
        IQueryHandler<GetTruckStatesQuery, Page<TruckState>> getTruckStates,
        IQueryHandler<GetTruckStateQuery, TruckState?> getTruckState,
        IQueryHandler<GetTruckQuery, Truck?> getTruck,
        IQueryHandler<ListAlertsQuery, Page<Alert>> listAlerts,
        IQueryHandler<GetTruckTelemetryQuery, IReadOnlyList<TruckTelemetry>> getTelemetry,
        ICommandHandler<AcknowledgeAlertCommand, Result> acknowledgeAlert,
        ILogger<FleetController> logger)
    {
        _getSummary       = getSummary;
        _getTruckStates   = getTruckStates;
        _getTruckState    = getTruckState;
        _getTruck         = getTruck;
        _listAlerts       = listAlerts;
        _getTelemetry     = getTelemetry;
        _acknowledgeAlert = acknowledgeAlert;
        _logger           = logger;
    }

    [HttpGet("summary", Name = "GetFleetSummary")]
    [Authorize(Policy = "FleetReader")]
    [ProducesResponseType(typeof(FleetSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FleetSummaryDto>> GetSummary(CancellationToken cancellationToken)
        => Ok(await _getSummary.Handle(new GetFleetSummaryQuery(), cancellationToken));

    [HttpGet("trucks", Name = "GetTruckStates")]
    [Authorize(Policy = "FleetReader")]
    [ProducesResponseType(typeof(Page<TruckState>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Page<TruckState>>> GetTruckStates(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
        => Ok(await _getTruckStates.Handle(new GetTruckStatesQuery(cursor, pageSize, status), cancellationToken));

    [HttpGet("trucks/{truckId}/state", Name = "GetTruckState")]
    [Authorize(Policy = "FleetReader")]
    [ProducesResponseType(typeof(TruckState), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TruckState>> GetTruckState(
        string truckId, CancellationToken cancellationToken)
    {
        var state = await _getTruckState.Handle(new GetTruckStateQuery(truckId), cancellationToken);
        if (state is null) return NotFound();
        return Ok(state);
    }

    [HttpGet("trucks/{truckId}", Name = "GetTruck")]
    [Authorize(Policy = "FleetReader")]
    [ProducesResponseType(typeof(Truck), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Truck>> GetTruck(
        string truckId, CancellationToken cancellationToken)
    {
        var truck = await _getTruck.Handle(new GetTruckQuery(truckId), cancellationToken);
        if (truck is null) return NotFound();
        return Ok(truck);
    }

    [HttpGet("trucks/{truckId}/telemetry", Name = "GetTruckTelemetry")]
    [Authorize(Policy = "FleetReader")]
    [ProducesResponseType(typeof(IReadOnlyList<TruckTelemetry>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<TruckTelemetry>>> GetTruckTelemetry(
        string truckId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var samples = await _getTelemetry.Handle(
                new GetTruckTelemetryQuery(truckId, from, to, limit),
                cancellationToken);
            return Ok(samples);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Validation failed", Detail = ex.Message });
        }
    }

    [HttpGet("alerts", Name = "ListAlerts")]
    [Authorize(Policy = "FleetReader")]
    [ProducesResponseType(typeof(Page<Alert>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Page<Alert>>> ListAlerts(
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? severity = null,
        [FromQuery] string? truckId = null,
        [FromQuery] bool onlyActive = true,
        CancellationToken cancellationToken = default)
        => Ok(await _listAlerts.Handle(
            new ListAlertsQuery(cursor, pageSize, severity, truckId, onlyActive),
            cancellationToken));

    [HttpPost("alerts/{id}/acknowledge", Name = "AcknowledgeAlert")]
    [Authorize(Policy = "AlertsAck")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AcknowledgeAlert(
        string id,
        [FromBody] AcknowledgeAlertBody body,
        CancellationToken cancellationToken)
    {
        var result = await _acknowledgeAlert.Handle(
            new AcknowledgeAlertCommand(id, body.AcknowledgedBy),
            cancellationToken);

        if (result.IsSuccess)
        {
            BffMetrics.AlertsAcknowledgedTotal.Add(1,
                new KeyValuePair<string, object?>("severity", "unknown"));
            return Ok();
        }

        return NotFound(new ProblemDetails { Title = "Acknowledge failed", Detail = result.Error?.Message });
    }
}

public sealed class AcknowledgeAlertBody
{
    public string AcknowledgedBy { get; set; } = string.Empty;
}
