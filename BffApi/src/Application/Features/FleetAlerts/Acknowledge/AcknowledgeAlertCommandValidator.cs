using FluentValidation;

namespace FleetStream.Application.Features.FleetAlerts.Acknowledge;

public sealed class AcknowledgeAlertCommandValidator : AbstractValidator<AcknowledgeAlertCommand>
{
    public AcknowledgeAlertCommandValidator()
    {
        RuleFor(c => c.AlertId)
            .NotEmpty()
            .MaximumLength(64);
        RuleFor(c => c.AcknowledgedBy)
            .NotEmpty()
            .MaximumLength(64);
    }
}