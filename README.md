# Expense Management API — .NET 8 + MongoDB 8

A reference project showing **MongoDB data modeling, indexing, aggregations and transactions
with .NET 8**, with comparisons to SQL Server / Entity Framework.
The domain is intentionally simple (users, categories, expenses) so the focus stays on how each
MongoDB concept is applied and how it maps to the relational world.

| Topic | Where to look |
|---|---|
| CRUD with the official driver | `Services/UserService.cs`, `CategoryService.cs`, `ExpenseService.cs` |
| Filtering, sorting, pagination | `ExpenseService.SearchAsync` / `BuildFilter` |
| Indexes | `Infrastructure/MongoIndexes.cs` |
| Aggregation pipelines | `Services/ReportService.cs` |
| Relationships and `$lookup` | `ExpenseService.SearchWithDetailsAsync`, `ReportService` |
| Transactions | `UserService.DeleteAsync` |
| `explain()` and query performance | `Services/DiagnosticsService.cs`, `scripts/mongo/explain-examples.js` |
| BSON mapping conventions | `Infrastructure/MongoDbConventions.cs` |

---

## Table of contents

1. [Quick start](#1-quick-start)
2. [Project structure and design decisions](#2-project-structure-and-design-decisions)
3. [API endpoints](#3-api-endpoints)
4. [Connecting MongoDB Compass](#4-connecting-mongodb-compass)
5. [Collections and documents](#5-collections-and-documents)
6. [CRUD with the .NET driver](#6-crud-with-the-net-driver)
7. [Queries, sorting and pagination](#7-queries-sorting-and-pagination)
8. [Indexes](#8-indexes)
9. [Aggregation pipelines](#9-aggregation-pipelines)
10. [Relationships: referencing vs embedding](#10-relationships-referencing-vs-embedding)
11. [Transactions](#11-transactions)
12. [Query performance with explain()](#12-query-performance-with-explain)
13. [MongoDB vs SQL Server](#13-mongodb-vs-sql-server)
14. [Migrating from MongoDB 6/7 to 8](#14-migrating-from-mongodb-67-to-8)
15. [Possible improvements](#15-possible-improvements)

---

## 1. Quick start

**Requirements:** Docker Desktop. (.NET 8 SDK or later only if you want to run the API or the tests outside Docker.)

```bash
docker compose up --build
```

| What | URL |
|---|---|
| Swagger UI | http://localhost:8080/swagger |
| Health check | http://localhost:8080/health |
| MongoDB (Compass / mongosh) | `mongodb://localhost:27017/?directConnection=true` |

On the first start the API:

1. Creates the indexes (idempotent, runs on every start).
2. Seeds **8 categories, 20 users and 50,000 expenses** (only if the database is empty). The large
   number of expenses is on purpose: it makes the difference between indexed and non-indexed queries visible.

Useful commands:

```bash
# Stop containers (data is kept in the named volume)
docker compose down

# Stop and DELETE the data (next start seeds again)
docker compose down -v

# Open a MongoDB shell inside the container
docker exec -it expense-mongo mongosh expense_management

# Run the explain() examples
docker exec -it expense-mongo mongosh expense_management --file /scripts/explain-examples.js
```

Run the API from your IDE against the MongoDB container (uses `appsettings.json`):

```bash
docker compose up -d mongo
dotnet run --project src/ExpenseManagement.Api
# http://localhost:5080/swagger
```

Run the tests (Docker must be running; Testcontainers starts its own MongoDB 8 replica set):

```bash
dotnet test
```

### Configuration

Strongly typed settings (`MongoDbSettings`) bound from the `MongoDb` section and validated at startup.
In Docker they come from environment variables (`__` is the section separator):

| Variable | Default (Docker) | Description |
|---|---|---|
| `MongoDb__ConnectionString` | `mongodb://mongo:27017/?replicaSet=rs0` | Connection string |
| `MongoDb__DatabaseName` | `expense_management` | Database name |
| `MongoDb__SeedData` | `true` | Seed sample data when the database is empty |
| `MongoDb__SeedExpenseCount` | `50000` | Number of generated expenses |

---

## 2. Project structure and design decisions

```
├── docker-compose.yml            # API + MongoDB 8 (single-node replica set) + volumes
├── scripts/mongo/                # mongosh scripts (mounted at /scripts in the container)
├── src/ExpenseManagement.Api/
│   ├── Controllers/              # Thin HTTP layer
│   ├── Services/                 # MongoDB code: queries, updates, pipelines, transactions
│   ├── Models/                   # Documents stored in MongoDB
│   ├── Dtos/                     # Request / response contracts
│   ├── Infrastructure/           # Settings, MongoDbContext, conventions, indexes, seeder, errors
│   ├── Common/                   # Exceptions and input guards
│   └── Dockerfile
└── tests/ExpenseManagement.Tests/
    ├── Unit/                     # No Docker needed
    └── Integration/              # Real MongoDB via Testcontainers + WebApplicationFactory
```

**Design decisions:**

- **`MongoClient` is a singleton.** It is thread-safe and owns the connection pool. Creating one per request
  (as you might with a scoped `DbContext`) wastes connections. `MongoDbContext` here is just a singleton
  holder for the client, the database and the typed collections.
- **No generic repository.** `IMongoCollection<T>` already plays the role of `DbSet<T>`. A generic
  `IRepository<T>` would hide exactly what matters in MongoDB (filter/update builders, sessions, pipelines,
  projections). Services use the collections directly; that keeps all MongoDB code easy to find.
- **No change tracking, no `SaveChanges`.** Every driver call is a round trip that executes immediately.
  You decide *how* to update (replace the document, `$set` some fields, `$inc` a counter...).
- **No migrations.** The schema lives in the C# classes. Indexes are created from code at startup.
  Evolving documents is done by tolerating old shapes (`IgnoreExtraElements`, nullable fields) or by
  running a one-off update script.
- **Errors** are mapped to RFC 7807 `ProblemDetails` by `GlobalExceptionHandler`
  (404 not found, 409 duplicate key / in use, 400 invalid input).

---

## 3. API endpoints

| Method | Route | MongoDB concept |
|---|---|---|
| GET | `/api/users?page=&pageSize=` | `Find` + `SortBy` + `Skip`/`Limit` + `CountDocuments` |
| GET | `/api/users/{id}` | `Find(...).FirstOrDefaultAsync` |
| POST | `/api/users` | `InsertOneAsync`, unique index → 409 |
| PUT | `/api/users/{id}` | `UpdateOneAsync` + `$set` |
| DELETE | `/api/users/{id}` | **Transaction**: delete user + all of their expenses |
| GET | `/api/users/{id}/expenses` | Compound index `{ userId: 1, date: -1 }` |
| GET | `/api/categories` | LINQ (`AsQueryable`) |
| POST | `/api/categories` | `InsertOneAsync` |
| PUT | `/api/categories/{id}` | `ReplaceOneAsync` (whole document) |
| DELETE | `/api/categories/{id}` | Manual "foreign key" check → 409 if in use |
| GET | `/api/expenses?userId=&categoryId=&from=&to=&sort=&page=&pageSize=` | Dynamic filter, sort, pagination |
| GET | `/api/expenses/details?...` | Same search + `$lookup` of user and category |
| GET | `/api/expenses/{id}` / `/{id}/details` | Find by id / `$lookup` |
| POST | `/api/expenses` | `InsertOneAsync` after validating references |
| PUT | `/api/expenses/{id}` | `ReplaceOneAsync` |
| PATCH | `/api/expenses/{id}` | Dynamic `$set` with `FindOneAndUpdateAsync` |
| DELETE | `/api/expenses/{id}` | `DeleteOneAsync` |
| GET | `/api/reports/totals-by-user?from=&to=` | `$match` → `$group` → `$sort` → `$lookup` → `$project` |
| GET | `/api/reports/totals-by-category?userId=&from=&to=` | `$group` by category + `$lookup` with sub-pipeline |
| GET | `/api/reports/monthly-totals?userId=&year=` | `$group` by `{ $year, $month }` |
| GET | `/api/diagnostics/explain/expenses-by-user?userId=` | `explain()` with index vs forced `COLLSCAN` |
| GET | `/api/diagnostics/explain/expenses-by-category?categoryId=` | Index used but in-memory `SORT` |
| GET | `/api/diagnostics/indexes` | `listIndexes` + `$indexStats` usage counters |

Notes: ids are 24-character hex strings (ObjectId). Date filters use `from` (inclusive) and `to`
(exclusive), e.g. `from=2025-01-01&to=2025-02-01` returns January. Dates without offset are treated as UTC.

A quick tour in Swagger: `GET /api/users` → copy an `id` → `GET /api/users/{id}/expenses` →
`GET /api/reports/totals-by-category?userId=...` → `GET /api/diagnostics/explain/expenses-by-user?userId=...`.

---

## 4. Connecting MongoDB Compass

1. Open Compass → **New connection**.
2. URI: `mongodb://localhost:27017/?directConnection=true`
3. Connect and open the `expense_management` database.

**Why `directConnection=true`?** MongoDB runs as a replica set whose only member is registered as
`mongo:27017` (the Docker service name, which is what the API container uses). When a client connects
to a replica set *without* `directConnection`, it asks the server for the member list and then tries to
connect to `mongo:27017`, which your machine cannot resolve. `directConnection=true` tells the client:
"talk only to the host I gave you".

Things to try in Compass:

- **Documents** tab: browse `expenses`, filter with `{ amount: { $gt: 1000 } }`, sort with `{ date: -1 }`.
- **Indexes** tab: see the indexes created by the API and their usage.
- **Explain Plan** tab: run `{ userId: ObjectId("...") }` and compare with a filter on `description`.
- **Aggregations** tab: paste a pipeline from `ReportService` or `scripts/mongo/queries.js`.
- **Schema** tab: Compass samples documents and infers the "schema" (there is no fixed one).
- **>_MONGOSH** (bottom panel): an embedded shell.

---

## 5. Collections and documents

| SQL Server | MongoDB |
|---|---|
| Database | Database |
| Table | **Collection** |
| Row | **Document** (BSON, a binary JSON with more types) |
| Column | Field |
| Primary key | `_id` (every document has one, indexed and unique) |
| Schema (DDL) | None enforced by default (optional JSON Schema validation) |
| `IDENTITY` / `NEWSEQUENTIALID()` | `ObjectId` generated by the driver |

A stored expense:

```js
{
  _id: ObjectId('6ae79bc84645d49ac3607540'),
  userId: ObjectId('6ae79bc64645d49ac360753e'),
  categoryId: ObjectId('6ae79bc74645d49ac360753f'),
  amount: Decimal128('15.75'),
  description: 'Lunch',
  date: ISODate('2025-03-10T00:00:00.000Z'),
  createdAt: ISODate('2026-11-01T20:06:32.133Z')
}
```

Key points:

- **Collections are created lazily** on the first insert or index creation. No `CREATE TABLE`.
- **Schema-less ≠ schema-free.** Documents in one collection *can* have different shapes; the
  application gives them structure. The C# classes are the schema.
- **`ObjectId`** (12 bytes): timestamp + random value + counter. It is generated client side, roughly
  ordered by creation time, and is unique without a round trip to the server.
- **BSON types matter.** `ObjectId("65f...")` and the string `"65f..."` are different values: a filter
  with the wrong type silently matches nothing. That is why references are stored as ObjectId
  (`[BsonRepresentation(BsonType.ObjectId)]`) and why raw pipelines use `ObjectId.Parse`.
- **Money as `Decimal128`.** The driver stores `decimal` as a *string* by default (no `$sum`, wrong
  ordering). `MongoDbConventions` registers `Decimal128` globally, the equivalent of `decimal(18,2)`.
- **Dates are UTC with millisecond precision.** .NET ticks are truncated (see `createdAt` above),
  and a `DateTime` with `Kind = Unspecified` would be treated as local time by the driver, so inputs are
  normalized with `Guard.ToUtc`.
- **Conventions** (`MongoDbConventions`): camelCase field names, ignore unknown fields (tolerates schema
  evolution), enums as strings. Comparable to EF model conventions.
- **Document size limit:** 16 MB per document.

---

## 6. CRUD with the .NET driver

| Operation | EF Core | MongoDB driver (this project) |
|---|---|---|
| Insert | `Add` + `SaveChanges` | `InsertOneAsync(doc)` / `InsertManyAsync(docs)` |
| Get by id | `Find(id)` | `Find(x => x.Id == id).FirstOrDefaultAsync()` |
| Query | LINQ to Entities | `Builders<T>.Filter...` or LINQ (`AsQueryable`) |
| Full update | Attach + modify + `SaveChanges` | `ReplaceOneAsync(filter, doc)` — replaces everything except `_id` |
| Partial update | Load + modify + `SaveChanges` | `UpdateOneAsync(filter, Builders<T>.Update.Set(...))` — no read needed |
| Update and return | — | `FindOneAndUpdateAsync(..., ReturnDocument.After)` (atomic) |
| Delete | `Remove` + `SaveChanges` | `DeleteOneAsync(filter)` / `DeleteManyAsync(filter)` |
| Exists | `AnyAsync` | `CountDocumentsAsync(filter, new CountOptions { Limit = 1 })` |
| Bulk insert | `SqlBulkCopy` / `AddRange` | `InsertManyAsync(docs, new InsertManyOptions { IsOrdered = false })` |

Results tell you what happened: `UpdateResult.MatchedCount` (found) vs `ModifiedCount` (changed),
`DeleteResult.DeletedCount`. A duplicate key raises `MongoWriteException` with
`ServerErrorCategory.DuplicateKey` (server error code 11000), which the services translate to 409.

`ReplaceOne` vs `UpdateOne` + `$set`: replace sends the whole document (any missing field disappears);
`$set` changes only the listed fields atomically on the server. Prefer `$set`/`$inc`/`$push` for
partial changes, they avoid read-modify-write races.

---

## 7. Queries, sorting and pagination

`ExpenseService.BuildFilter` composes a filter dynamically, like chaining `.Where()` on an `IQueryable`:

```csharp
filters.Add(builder.Eq(e => e.UserId, query.UserId));        // { userId: ObjectId(...) }
filters.Add(builder.Gte(e => e.Date, from));                  // { date: { $gte: ... } }
filters.Add(builder.Lt(e => e.Date, to));                     // { date: { $lt: ... } }
return builder.And(filters);
```

```csharp
await _expenses.Find(filter).Sort(sort).Skip(skip).Limit(pageSize).ToListAsync();
await _expenses.CountDocumentsAsync(filter);
```

The mongosh equivalent:

```js
db.expenses.find({ userId: ObjectId("..."), date: { $gte: ISODate("2025-01-01"), $lt: ISODate("2025-02-01") } })
           .sort({ date: -1 }).skip(20).limit(20)
```

**Pagination:** `skip/limit` is the same as `OFFSET/FETCH` and has the same problem: the server still
walks over the skipped entries, so deep pages get slower. For large data sets use **keyset pagination**
(`date < lastSeenDate`), which an index can serve directly. Also note that sorting only by `date` is not
fully deterministic when two expenses share the same timestamp; a tie-breaker (`_id`) fixes that, at the
cost of needing it in the index too.

---

## 8. Indexes

Without an index MongoDB performs a **COLLSCAN** (a full table scan). Indexes are B-trees, just like
SQL Server non-clustered indexes. Created in `MongoIndexes.cs`:

```csharp
Builders<User>.IndexKeys.Ascending(u => u.Email), new CreateIndexOptions { Unique = true }
Builders<Expense>.IndexKeys.Ascending(e => e.UserId).Descending(e => e.Date)
Builders<Expense>.IndexKeys.Ascending(e => e.CategoryId)
```

| Index | Why |
|---|---|
| `ux_users_email` `{ email: 1 }` unique | Equivalent to a `UNIQUE` constraint. Guarantees no duplicate emails even under concurrent requests (an "exists?" check in code alone has a race), and makes lookups by email fast. Emails are stored lowercased so the check is case-insensitive (alternative: an index with a case-insensitive *collation*). |
| `ix_expenses_userId_date` `{ userId: 1, date: -1 }` | Serves the most common query: *expenses of a user, optionally in a date range, newest first*. The index is already ordered by date inside each user, so no in-memory sort is needed and `limit(20)` reads only 20 entries. Its **prefix** `{ userId }` also serves "all expenses of a user" and the `$match` of the per-user reports. |
| `ix_expenses_categoryId` `{ categoryId: 1 }` | Filters by category, per-category reports, and the "is this category in use?" check before deleting one. Like indexing a foreign key column in SQL Server, which is also not automatic. |
| `ux_categories_name` `{ name: 1 }` unique | Keeps category names unique. |

Rules of thumb (they apply to SQL Server too):

- **ESR rule** for compound indexes: **E**quality fields first, then **S**ort fields, then **R**ange fields.
- **Prefix rule:** `{ userId, date }` serves queries on `userId` and on `userId + date`, but **not** on `date` alone.
- **Indexes cost writes and RAM.** Every insert updates every index. Index what your queries need.
- **Creating indexes from code is idempotent**: if an identical index exists, nothing happens. Changing the
  options of an existing index with the same name fails, so drop it first.
- `GET /api/diagnostics/indexes` shows every index and how many times it was used (`$indexStats`).

---

## 9. Aggregation pipelines

A pipeline is a list of **stages**. Documents flow through them; each stage transforms the stream.
Everything runs on the server and only the final result travels to the API, so there is no need to load
50,000 documents into memory to sum them.

| Stage | SQL equivalent |
|---|---|
| `$match` | `WHERE` (use it first, it can use indexes) |
| `$group` | `GROUP BY` with `$sum`, `$avg`, `$min`, `$max`, `$count`... |
| `$sort` | `ORDER BY` |
| `$project` / `$addFields` | `SELECT` list, computed columns |
| `$lookup` | `LEFT OUTER JOIN` |
| `$unwind` | Flatten an array into one document per element |
| `$skip` / `$limit` | `OFFSET` / `FETCH` |
| `$facet` | Several aggregations over the same input in one query |
| `$count` | `COUNT(*)` |

Totals per user (`ReportService.GetTotalsByUserAsync`), in mongosh syntax:

```js
db.expenses.aggregate([
  { $match: { date: { $gte: ISODate("2025-01-01"), $lt: ISODate("2026-01-01") } } },
  { $group: { _id: "$userId", total: { $sum: "$amount" }, count: { $sum: 1 }, average: { $avg: "$amount" } } },
  { $sort: { total: -1 } },
  { $lookup: { from: "users", localField: "_id", foreignField: "_id", as: "user" } },
  { $unwind: { path: "$user", preserveNullAndEmptyArrays: true } },
  { $project: { _id: 0, userId: "$_id", userName: "$user.name", total: 1, count: 1, average: { $round: ["$average", 2] } } }
])
```

Equivalent SQL:

```sql
SELECT e.UserId, u.Name, SUM(e.Amount), COUNT(*), AVG(e.Amount)
FROM Expenses e LEFT JOIN Users u ON u.Id = e.UserId
WHERE e.Date >= '2025-01-01' AND e.Date < '2026-01-01'
GROUP BY e.UserId, u.Name
ORDER BY SUM(e.Amount) DESC
```

Notes:

- **Order matters.** `$match` first (fewer documents, index usage). **Group before `$lookup`** so the join
  runs 20 times (once per user), not 50,000 times.
- Monthly totals group by a **compound `_id`**: `{ year: { $year: "$date" }, month: { $month: "$date" } }`.
- In the C# code the stages are `BsonDocument`s, so they read like mongosh and can be pasted into
  Compass. The driver also has a typed fluent API (`Aggregate().Match(...).Group(...)`), used in
  `ExpenseService.SearchWithDetailsAsync` together with raw BSON stages.
- Each stage has a 100 MB memory limit; since MongoDB 6.0 stages spill to disk automatically when needed.

---

## 10. Relationships: referencing vs embedding

This project **references**: `expenses.userId` and `expenses.categoryId` hold the `_id` of the related
documents, like foreign keys. But:

- **MongoDB does not enforce references.** No FK constraints, no `ON DELETE CASCADE`. The application must:
  - check that the user and the category exist before saving an expense (`ExpenseService.ValidateReferencesAsync`);
  - refuse to delete a category that is still in use (`CategoryService.DeleteAsync`);
  - delete a user's expenses together with the user (`UserService.DeleteAsync`, in a transaction).
- **Joins are done with `$lookup`** inside an aggregation pipeline (`GET /api/expenses/details`):

```js
{ $lookup: { from: "users", localField: "userId", foreignField: "_id",
             pipeline: [ { $project: { name: 1, email: 1 } } ],   // only the fields we need
             as: "user" } },
{ $unwind: { path: "$user", preserveNullAndEmptyArrays: true } }   // array -> object, LEFT JOIN semantics
```

  Paginate **before** `$lookup` so only the 20 documents of the page are joined. `foreignField: "_id"` is
  always indexed; when joining on another field, index it.

**Embedding** is the alternative: store related data inside the same document.

```js
// Embedded alternative: a user document that contains its expenses
{ _id: ObjectId("..."), name: "Ana", email: "ana@example.com",
  expenses: [ { amount: Decimal128("12.50"), category: { _id: ObjectId("..."), name: "Food" }, date: ISODate("...") }, ... ] }
```

| | Referencing (this project) | Embedding |
|---|---|---|
| Reads | Need `$lookup` or a second query | One read returns everything |
| Writes | Each entity updated on its own | One atomic write updates parent + children |
| Duplication | None | Copies (e.g. category name) must be kept in sync |
| Growth | Unbounded is fine | Bounded: 16 MB per document; large arrays are slow to update |
| Independent access | Expenses can be queried and indexed alone | Children are reached through the parent |

**Guideline:** embed what is read together and owned by the parent ("an order and its lines", "an address");
reference what grows without limit, is shared, or is queried on its own. Expenses grow without limit and are
queried across users (reports), so they are a separate collection. A common hybrid is **extended reference**:
keep the `categoryId` *and* a copy of the category name in the expense to avoid the `$lookup` on reads,
accepting that renames must update the copies.

---

## 11. Transactions

**Single-document operations are always atomic** in MongoDB, including updates to embedded arrays. That is
why good document design often removes the need for transactions. When an operation must change several
documents (or collections) all-or-nothing, use a **multi-document transaction**.

Example: `DELETE /api/users/{id}` deletes the user and all of their expenses (`UserService.DeleteAsync`):

```csharp
using var session = await client.StartSessionAsync();
var deleted = await session.WithTransactionAsync(async (s, ct) =>
{
    var user = await users.DeleteOneAsync(s, filterUser, cancellationToken: ct);      // note: session "s"
    if (user.DeletedCount == 0) throw new NotFoundException(...);                      // aborts the transaction
    var expenses = await expenses.DeleteManyAsync(s, filterExpenses, cancellationToken: ct);
    return expenses.DeletedCount;
}, new TransactionOptions(ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority));
```

| SQL Server / ADO.NET | MongoDB driver |
|---|---|
| `SqlConnection` | `IClientSessionHandle` (a session) |
| `BeginTransaction()` | `session.StartTransaction()` (or `WithTransactionAsync`) |
| Pass the transaction to every `SqlCommand` | Pass the **session** to every operation |
| `Commit()` / `Rollback()` | `CommitTransactionAsync()` / `AbortTransactionAsync()` |
| Retry on deadlock (manually) | `WithTransactionAsync` retries automatically on transient errors |

**Why a replica set is required.** Transactions are built on the replica set machinery: the **oplog**
(operation log that records the committed transaction as a unit for replication) and **snapshot reads**
with majority read/write concerns. A standalone `mongod` has no oplog, so it rejects transactions with
*"Transaction numbers are only allowed on a replica set member or mongos"*. Locally we therefore run a
**single-node replica set**:

```yaml
command: ["--replSet", "rs0", "--bind_ip_all"]
healthcheck:   # initiates the replica set once and reports healthy when the node is PRIMARY
  test: mongosh --eval "try { rs.status() } catch (e) { rs.initiate({ _id: 'rs0', members: [{ _id: 0, host: 'mongo:27017' }] }) }; quit(db.hello().isWritablePrimary ? 0 : 1)"
```

The API waits for `service_healthy` before starting and connects with `?replicaSet=rs0`.

Things to know:

- **Forgetting the session** in one call silently runs that operation *outside* the transaction.
- Transactions have a default **60-second** lifetime limit and keep changes in the WiredTiger cache; keep them short and small.
- Concurrent transactions touching the same document get a **WriteConflict** (transient, retried by `WithTransactionAsync`).
- Isolation: reads inside the transaction see a snapshot. A concurrent `POST /api/expenses` that validated
  the user *before* the delete committed can still insert an orphan expense afterwards. Absolute integrity
  would require a different design (e.g. embedding, or a status flag checked atomically).
- Read concern `snapshot` + write concern `majority` give the strongest guarantees; they are the
  recommended options for transactions.

---

## 12. Query performance with explain()

`explain("executionStats")` shows the plan chosen by the query planner and what it cost.

| Field | Meaning |
|---|---|
| `winningPlan.stage` | Plan tree: `IXSCAN` (index), `COLLSCAN` (full scan), `FETCH` (read document), `SORT` (in-memory sort), `LIMIT`... |
| `nReturned` | Documents returned |
| `totalKeysExamined` | Index entries read |
| `totalDocsExamined` | Documents read |
| `executionTimeMillis` | Time on the server |

A healthy query has `nReturned ≈ totalKeysExamined ≈ totalDocsExamined`. MongoDB 8 executes many queries
with the **Slot-Based Execution engine (SBE)**; then the classic plan tree is nested under
`winningPlan.queryPlan` (the code handles both shapes).

**Try it:** `GET /api/diagnostics/explain/expenses-by-user?userId=...` runs the same query twice: once with
the planner's choice and once with `hint({ $natural: 1 })`, which forces a collection scan. Results with the
seeded data (50,000 expenses):

| Query | Plan | Keys examined | Docs examined | Returned |
|---|---|---|---|---|
| user, newest 20 (index) | `LIMIT > FETCH > IXSCAN(ix_expenses_userId_date)` | 20 | 20 | 20 |
| user, newest 20 (`$natural` hint) | `SORT > COLLSCAN` | 0 | 50,000 | 20 |
| category, newest 20 (index) | `SORT > FETCH > IXSCAN(ix_expenses_categoryId)` | ~6,300 | ~6,300 | 20 |
| date range only | `COLLSCAN` | 0 | 50,000 | ~2,100 |

What this shows:

- The compound index reads **only the 20 entries it returns**: it filters *and* provides the order.
- The category index finds the right documents but has no `date`, so all ~6,300 matches are sorted in
  memory to return 20. A `{ categoryId: 1, date: -1 }` index would remove the `SORT` stage (try it, the
  commands are at the end of `explain-examples.js`).
- A filter on `date` alone cannot use `{ userId, date }` (prefix rule) → full scan.

The same in mongosh:

```js
db.expenses.find({ userId: ObjectId("...") }).sort({ date: -1 }).limit(20).explain("executionStats")
db.expenses.find({ userId: ObjectId("...") }).sort({ date: -1 }).limit(20).hint({ $natural: 1 }).explain("executionStats")
db.expenses.explain("executionStats").aggregate([{ $match: { userId: ObjectId("...") } }, { $group: { _id: "$categoryId", total: { $sum: "$amount" } } }])
```

Run all examples at once:

```bash
docker exec -it expense-mongo mongosh expense_management --file /scripts/explain-examples.js
```

The driver has no `Explain()` method on `Find`, so `DiagnosticsService` sends the command directly:
`{ explain: { find: "expenses", filter, sort, limit, hint }, verbosity: "executionStats" }`.

---

## 13. MongoDB vs SQL Server

| Topic | SQL Server | MongoDB |
|---|---|---|
| Data model | Tables, rows, fixed schema | Collections of BSON documents, flexible schema |
| Schema changes | DDL + EF migrations | Code changes; optional JSON Schema validation; scripts for data backfill |
| Relationships | Foreign keys, joins | References (`$lookup`) or embedding; no FK enforcement |
| Normalization | Normalize, join at read time | Model for the access pattern; controlled duplication is normal |
| Query language | T-SQL | MQL (filter documents) + aggregation pipeline |
| Atomicity | Any statement / transaction | Single document always; multi-document via transactions |
| Transactions | Always available | Require replica set or sharded cluster; keep them short |
| Identity | `IDENTITY`, sequences, GUIDs | `ObjectId` generated by the client |
| Decimal | `decimal(p,s)` | `Decimal128` (configure the driver, default is string) |
| Dates | `datetime2`, `datetimeoffset`, time zones | UTC date with millisecond precision |
| Indexes | Clustered + non-clustered, include columns | B-tree, compound, multikey (arrays), text, geo, TTL, partial, wildcard |
| Scale out | Mostly scale up; read replicas (AG) | Replica sets for HA, **sharding** for horizontal scale |
| ORM | EF Core: change tracking, `SaveChanges`, migrations | Driver: explicit operations, no tracking (an EF Core provider exists, with limitations) |
| Execution plan | Actual execution plan | `explain("executionStats")` |

---

## 14. Migrating from MongoDB 6/7 to 8

Points to review when an existing application moves to MongoDB 8.0. Always confirm against the official
[8.0 compatibility changes](https://www.mongodb.com/docs/manual/release-notes/8.0-compatibility/) and
the upgrade procedure for your deployment type.

**Upgrade path**

- Major versions are upgraded **one at a time**: 6.0 → 7.0 → 8.0. A 6.0 deployment cannot jump to 8.0.
- Each step has two parts: replace the binaries, then raise the **featureCompatibilityVersion**:
  `db.adminCommand({ setFeatureCompatibilityVersion: "8.0", confirm: true })`. Keep the old FCV for a while
  after the binary upgrade: it keeps a downgrade possible. Once FCV is raised, new on-disk features may be
  used and downgrading becomes much harder. Take a backup first.
- Upgrade replica set members one at a time (secondaries first, then step down the primary).

**Driver (the part most likely to break a .NET migration)**

- Use a driver version that supports server 8.0 (check the driver compatibility matrix; this project uses
  MongoDB.Driver 3.x). Legacy .NET Framework applications are often on old 2.x drivers.
- Driver **3.0 breaking changes** to plan for:
  - The old **LINQ2 provider was removed**; only LINQ3 remains. Some LINQ expressions translate differently or
    throw `ExpressionNotSupportedException`. Test every LINQ query.
  - **GUID representation**: the legacy `GuidRepresentationMode.V2` was removed and the default representation
    is `Unspecified`, so serializing a `Guid` fails until a representation is configured (typically
    `new GuidSerializer(GuidRepresentation.Standard)`). Data written by old drivers as legacy C# GUIDs
    (binary subtype 3) needs `GuidRepresentation.CSharpLegacy` or a data migration, otherwise lookups by
    GUID stop matching.
  - Other removed obsolete APIs; review the driver's upgrade guide.
- `decimal` defaults: if the legacy data stored `decimal` as strings (driver default), changing to
  `Decimal128` (as this project does) requires migrating existing documents.

**Server behavior**

- **Query execution:** 8.0 extends the Slot-Based Execution engine and changes the optimizer. Plans can differ,
  and `explain()` output has a different shape (`winningPlan.queryPlan`, `slotBasedPlan`). Tools or tests that
  parse explain output must handle both. Re-check the plans of critical queries after upgrading.
- **Query settings:** 8.0 introduces `setQuerySettings` (persistent per-query-shape settings such as index hints)
  and deprecates **index filters**. Review any `planCacheSetFilter` usage.
- **Removed / deprecated features and parameters:** review server parameters, deprecated operators and
  commands listed in the compatibility notes, and startup options in configuration files (unknown options
  prevent `mongod` from starting).
- **New capabilities to take advantage of:** better general performance, the cross-collection `bulkWrite`
  command (driver support: `client.BulkWriteAsync`), `sort` option for `updateOne`/`replaceOne`, and
  Queryable Encryption range queries.

**Checklist**

1. Upgrade the driver first (still against the old server) and run the full test suite.
2. Restore a production backup into a staging environment with the new version.
3. Compare `explain()` plans and timings of the most important queries before and after.
4. Upgrade binaries, observe, then raise FCV.
5. Integration tests against the target version (this project uses Testcontainers with `mongo:8.0`).

---

## 15. Possible improvements

- Index `{ categoryId: 1, date: -1 }` to remove the in-memory sort of category queries (compare with `GET /api/diagnostics/explain/expenses-by-category`).
- **Keyset pagination** (`date < lastDate`) instead of skip/limit for deep pages.
- `$facet` to return totals per category and per month in a single aggregation.
- JSON Schema validation on the `expenses` collection (`collMod` with `validator`).
- Extended reference: store the category name in each expense and update the copies in a transaction on rename.
- TTL index on an audit collection for automatic expiration.
