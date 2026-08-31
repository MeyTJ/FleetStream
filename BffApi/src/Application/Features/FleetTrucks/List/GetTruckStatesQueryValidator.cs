using FluentValidation;

namespace FleetStream.Application.Features.FleetTrucks.List;

public sealed class GetTruckStatesQueryValidator : AbstractValidator<GetTruckStatesQuery>
{
    private static readonly HashSet<string> ValidStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "Active", "Maintenance", "Retired" };

    public GetTruckStatesQueryValidator()
    {
        RuleFor(q => q.PageSize).InclusiveBetween(1, 200);
        RuleFor(q => q.Status)
            .Must(s => s is null || ValidStatuses.Contains(s))
            .WithMessage("status must be Active, Maintenance, or Retired.");
    }
}
