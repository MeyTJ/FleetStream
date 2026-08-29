using FluentValidation;

namespace FleetStream.Application.Features.FleetTrucks.GetTruck;

public sealed class GetTruckQueryValidator : AbstractValidator<GetTruckQuery>
{
    public GetTruckQueryValidator()
    {
        RuleFor(q => q.TruckId)
            .NotEmpty()
            .MaximumLength(64)
            .Matches("^[A-Za-z0-9\\-_:.]+$")
            .WithMessage("TruckId must be URL-safe (letters, digits, '-', '_', ':', '.').");
    }
}