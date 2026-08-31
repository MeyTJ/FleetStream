using System.Security.Claims;
using System.Text.RegularExpressions;

namespace FleetStream.Infrastructure.Security;

public static class TruckIdValidation
{
    public static readonly Regex Pattern = new(@"^[A-Za-z0-9\-_:.]+$", RegexOptions.Compiled);

    public static bool IsValid(string? truckId) =>
        !string.IsNullOrWhiteSpace(truckId)
        && truckId.Length <= 64
        && Pattern.IsMatch(truckId);

    public static bool IsAllowedForUser(string truckId, ClaimsPrincipal? user)
    {
        var allowed = user?.FindAll("truckIds").Select(c => c.Value).ToList();
        return allowed is null or { Count: 0 } || allowed.Contains(truckId);
    }
}
