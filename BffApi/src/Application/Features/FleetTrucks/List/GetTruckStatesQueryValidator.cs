using FluentValidation;

namespace FleetStream.Application.Features.FleetTrucks.List;

/// <summary>
/// Validates <see cref="GetTruckStatesQuery"/>. Lives next to the query in
/// the same slice so the contract is discoverable in one place.
/// </summary>
public sealed class GetTruckStatesQueryValidator : AbstractValidator<GetTruckStatesQuery>
{
    public GetTruckStatesQueryValidator()
    {
        RuleFor(q => q.Skip).GreaterThanOrEqualTo(0);
        RuleFor(q => q.Take).InclusiveBetween(1, 1_000);
    }
}