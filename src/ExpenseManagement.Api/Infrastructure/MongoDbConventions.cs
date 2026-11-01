using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace ExpenseManagement.Api.Infrastructure;

/// <summary>
/// Global BSON serialization rules. This is the closest equivalent to EF Core's
/// OnModelCreating / model conventions, but it only controls how C# objects are
/// mapped to BSON documents. MongoDB itself has no schema to migrate.
/// </summary>
public static class MongoDbConventions
{
    private static bool _registered;
    private static readonly object Lock = new();

    public static void Register()
    {
        lock (Lock)
        {
            if (_registered) return;

            var pack = new ConventionPack
            {
                // C# "CreatedAt" is stored as "createdAt", the usual MongoDB style.
                new CamelCaseElementNameConvention(),
                // Documents may have fields our classes do not know about (schema-less).
                new IgnoreExtraElementsConvention(true),
                // Store enums as readable strings instead of integers.
                new EnumRepresentationConvention(BsonType.String)
            };
            ConventionRegistry.Register("ExpenseManagementConventions", pack, _ => true);

            // By default the driver stores decimal as a string. Decimal128 keeps it numeric,
            // so $sum, $avg and range queries work, and it avoids double rounding errors.
            // This is the equivalent of decimal(18,2) in SQL Server.
            BsonSerializer.TryRegisterSerializer(new DecimalSerializer(BsonType.Decimal128));

            _registered = true;
        }
    }
}
