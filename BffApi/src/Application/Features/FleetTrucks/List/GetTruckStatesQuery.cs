using System.ComponentModel.DataAnnotations;
using FleetStream.Core.Domain.Entities;

namespace FleetStream.Application.Features.FleetTrucks.List;

public sealed record GetTruckStatesQuery(
    [property: Range(0, int.MaxValue)] int Skip = 0,
    [property: Range(1, 1_000)]      int Take = 100);