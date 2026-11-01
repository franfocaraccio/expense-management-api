using System.Text.Json;

namespace ExpenseManagement.Api.Dtos;

/// <summary>The most relevant numbers of an explain("executionStats") result.</summary>
/// <param name="Stages">Plan stages from the root to the leaf, e.g. "LIMIT > FETCH > IXSCAN".</param>
/// <param name="IndexName">Index used by the IXSCAN stage, if any.</param>
/// <param name="UsesIndex">True when the plan contains an IXSCAN stage.</param>
/// <param name="HasInMemorySort">True when the plan contains a blocking SORT stage.</param>
/// <param name="NReturned">Documents returned.</param>
/// <param name="TotalKeysExamined">Index entries read.</param>
/// <param name="TotalDocsExamined">Documents read from the collection.</param>
/// <param name="ExecutionTimeMillis">Server execution time.</param>
/// <param name="Raw">Full explain output (only when includeRaw=true).</param>
public record ExplainSummaryDto(
    string Stages,
    string? IndexName,
    bool UsesIndex,
    bool HasInMemorySort,
    long NReturned,
    long TotalKeysExamined,
    long TotalDocsExamined,
    long ExecutionTimeMillis,
    JsonElement? Raw);

/// <param name="Query">The same query in mongosh syntax, ready to paste into a shell.</param>
/// <param name="WithIndex">Plan chosen by the query planner.</param>
/// <param name="WithoutIndex">Plan forced to a collection scan with hint { $natural: 1 }.</param>
public record ExplainComparisonDto(string Query, ExplainSummaryDto WithIndex, ExplainSummaryDto WithoutIndex);

/// <param name="Collection">Collection name.</param>
/// <param name="Name">Index name.</param>
/// <param name="Keys">Index key pattern, e.g. { "userId": 1, "date": -1 }.</param>
/// <param name="Unique">Whether the index enforces uniqueness.</param>
/// <param name="Operations">Times the index was used since the server started ($indexStats).</param>
public record IndexInfoDto(string Collection, string Name, JsonElement Keys, bool Unique, long? Operations);
