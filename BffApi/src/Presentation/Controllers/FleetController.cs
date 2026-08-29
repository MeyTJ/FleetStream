using Asp.Versioning;
using FleetStream.Application.Features.FleetAlerts.Acknowledge;
using FleetStream.Application.Features.FleetSummary.Get;
using FleetStream.Application.Features.FleetTrucks.GetState;
using FleetStream.Application.Features.FleetTrucks.GetTruck;
using FleetStream.Application.Features.FleetTrucks.List;
using FleetStream.Application.Shared.Messaging;
using FleetStream.Application.Shared.Results;
using FleetStream.Core.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FleetStream.Presentation.Controllers;

/// <summary>
/// Versioned REST surface for fleet operations. Authorization is
/// policy-based (see <c>Program.cs</c>): <c>FleetReader</c> for read
/// endpoints, <c>FleetAdmin</c> for write endpoints, <c>AlertsAck</c> for
/// acknowledgement. Each action depends on a single, explicit handler
/// interface — there is no in-process mediator, no service-layer
/// indirection. This is the Vertical Slice flavour the project uses.
/// </summary>
[ApiController]
[Authorize]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/fleet")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
public sealed class FleetController : ControllerBase
{
    private readonly IQueryHandler<GetFleetSummaryQuery,  FleetSummaryDto>    _getSummary;
    private readonly IQueryHandler<GetTruckStatesQuery,   IReadOnlyList<TruckState>> _getTruckStates;
    private readonly IQueryHandler<GetTruckStateQuery,    TruckState?>        _getTruckState;
    private readonly IQueryHandler<GetTruckQuery,         Truck?>            _getTruck;
    private readonly ICommandHandler<AcknowledgeAlertCommand, Result>          _acknowledgeAlert;
    private readonly ILogger<FleetController> _logger;

    public FleetController(
        IQueryHandler<GetFleetSummaryQuery, FleetSummaryDto> getSummary,
        IQueryHandler<GetTruckStatesQuery, IReadOnlyList<TruckState>> getTruckStates,
        IQueryHandler<GetTruckStateQuery, TruckState?> getTruckState,
        IQueryHandler<GetTruckQuery, Truck?> getTruck,
        ICommandHandler<AcknowledgeAlertCommand, Result> acknowledgeAlert,
        ILogger<FleetController> logger)
    {
        _getSummary      = getSummary;
        _getTruckStates  = getTruckStates;
        _getTruckState   = getTruckState;
        _getTruck        = getTruck;
        _acknowledgeAlert = acknowledgeAlert;
        _logger          = logger;
    }

    [HttpGet("summary", Name = "GetFleetSummary")]
    [Authorize(Policy = "FleetReader")]
    [ProducesResponseType(typeof(FleetSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FleetSummaryDto>> GetSummary(CancellationToken cancellationToken)
        => Ok(await _getSummary.Handle(new GetFleetSummaryQuery(), cancellationToken));

    [HttpGet("trucks", Name = "GetTruckStates")]
    [Authorize(Policy = "FleetReader")]
    [ProducesResponseType(typeof(IReadOnlyList<TruckState>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TruckState>>> GetTruckStates(
        [FromQuery] int skip, [FromQuery] int take, CancellationToken cancellationToken)
        => Ok(await _getTruckStates.Handle(new GetTruckStatesQuery(skip, take), cancellationToken));

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

        return result.IsSuccess
            ? Ok()
            : NotFound(new ProblemDetails { Title = "Acknowledge failed", Detail = result.Error?.Message });
    }
}

/// <summary>
/// Body for the <c>POST /api/v1/fleet/alerts/{id}/acknowledge</c> endpoint.
/// </summary>
public sealed class AcknowledgeAlertBody
{
    public string AcknowledgedBy { get; set; } = string.Empty;
}