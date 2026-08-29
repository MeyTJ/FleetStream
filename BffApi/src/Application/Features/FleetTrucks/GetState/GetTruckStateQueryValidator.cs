using FluentValidation;

namespace FleetStream.Application.Features.FleetTrucks.GetState;

public sealed class GetTruckStateQueryValidator : AbstractValidator<GetTruckStateQuery>
{
    public GetTruckStateQueryValidator()
    {
        RuleFor(q => q.TruckId)
            .NotEmpty()
            .MaximumLength(64)
            .Matches("^[A-Za-z0-9\\-_:.]+$")
            .WithMessage("TruckId must be URL-safe (letters, digits, '-', '_', ':', '.').");
    }
}