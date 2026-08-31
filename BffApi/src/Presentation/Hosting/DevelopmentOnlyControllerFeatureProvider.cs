using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace FleetStream.Presentation.Hosting;

internal sealed class DevelopmentOnlyControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    private readonly IHostEnvironment _env;
    private readonly HashSet<Type> _developmentOnly;

    public DevelopmentOnlyControllerFeatureProvider(IHostEnvironment env, params Type[] developmentOnlyControllers)
    {
        _env = env;
        _developmentOnly = developmentOnlyControllers.ToHashSet();
    }

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        if (_env.IsDevelopment())
            return;

        var remove = feature.Controllers
            .Where(c => _developmentOnly.Contains(c.AsType()))
            .ToList();

        foreach (var controller in remove)
            feature.Controllers.Remove(controller);
    }
}
