using System.ComponentModel.DataAnnotations;
using FleetStream.Core.Domain.Entities;

namespace FleetStream.Application.Features.FleetTrucks.GetTruck;

public sealed record GetTruckQuery(
    [property: Required, StringLength(64, MinimumLength = 1)]
    [property: RegularExpression("^[A-Za-z0-9\\-_:.]+$", ErrorMessage = "TruckId must be URL-safe.")]
    string TruckId);