using FluentValidation;

namespace FleetStream.Application.Features.FleetAlerts.List;

public sealed class ListAlertsQueryValidator : AbstractValidator<ListAlertsQuery>
{
    public ListAlertsQueryValidator()
    {
        RuleFor(q => q.PageSize).InclusiveBetween(1, 500);
        RuleFor(q => q.TruckId)
            .MaximumLength(64)
            .When(q => !string.IsNullOrWhiteSpace(q.TruckId));
    }
}
