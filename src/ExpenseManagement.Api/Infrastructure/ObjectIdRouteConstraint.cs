using System.Globalization;
using MongoDB.Bson;

namespace ExpenseManagement.Api.Infrastructure;

/// <summary>
/// Route constraint "{id:objectid}": routes with an invalid id do not match (404),
/// so controllers never receive a value the driver cannot convert to ObjectId.
/// </summary>
public class ObjectIdRouteConstraint : IRouteConstraint
{
    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey,
        RouteValueDictionary values, RouteDirection routeDirection)
    {
        return values.TryGetValue(routeKey, out var value)
               && ObjectId.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out _);
    }
}
