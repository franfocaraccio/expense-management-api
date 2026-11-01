using MongoDB.Bson;

namespace ExpenseManagement.Api.Common;

public static class Guard
{
    /// <summary>
    /// Ids that come from the body or query string must be valid ObjectIds (24 hex chars).
    /// Otherwise the driver throws a FormatException while serializing the filter.
    /// </summary>
    public static void ValidObjectId(string? value, string name)
    {
        if (!ObjectId.TryParse(value, out _))
        {
            throw new BadRequestException($"'{name}' must be a valid ObjectId (24 hexadecimal characters).");
        }
    }

    public static void ValidDateRange(DateTime? from, DateTime? to)
    {
        if (from.HasValue && to.HasValue && from.Value >= to.Value)
        {
            throw new BadRequestException("'from' must be earlier than 'to'.");
        }
    }

    /// <summary>
    /// BSON dates are always UTC. A date without offset ("2025-03-01") arrives as
    /// DateTimeKind.Unspecified and the driver would treat it as local time, so we
    /// declare it UTC explicitly to get the same result on every machine.
    /// </summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public static DateTime? ToUtc(DateTime? value) => value.HasValue ? ToUtc(value.Value) : null;
}
