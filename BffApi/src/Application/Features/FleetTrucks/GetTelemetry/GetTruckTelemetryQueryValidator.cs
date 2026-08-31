using FluentValidation;

namespace FleetStream.Application.Features.FleetTrucks.GetTelemetry;

public sealed class GetTruckTelemetryQueryValidator : AbstractValidator<GetTruckTelemetryQuery>
{
    public GetTruckTelemetryQueryValidator()
    {
        RuleFor(q => q.TruckId).NotEmpty().MaximumLength(64);
        RuleFor(q => q.Limit).InclusiveBetween(1, 1000);
        RuleFor(q => q)
            .Must(q => q.From is null || q.To is null || q.From < q.To)
            .WithMessage("from must be before to.");
        RuleFor(q => q)
            .Must(q => q.From is null || q.To is null || q.To - q.From <= TimeSpan.FromHours(24))
            .WithMessage("time window must not exceed 24 hours.");
    }
}
