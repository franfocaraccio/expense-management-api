// explain() examples. Run them in mongosh or in the MongoDB Compass shell (">_MONGOSH" tab):
//   docker exec -it expense-mongo mongosh expense_management --file /scripts/explain-examples.js
// (docker-compose mounts this folder at /scripts) or paste the blocks below one by one.
//
// Key fields of explain("executionStats"):
//   queryPlanner.winningPlan       -> plan tree: IXSCAN (index) vs COLLSCAN (full scan), SORT (in memory)
//   executionStats.nReturned        -> documents returned
//   executionStats.totalKeysExamined-> index entries read
//   executionStats.totalDocsExamined-> documents read
//   executionStats.executionTimeMillis
// A good query has nReturned ~ totalKeysExamined ~ totalDocsExamined.

const userId = db.users.findOne({}, { _id: 1 })._id;
const categoryId = db.categories.findOne({ name: "Food" })._id;

// With the Slot-Based Execution engine the classic plan tree is nested under "queryPlan".
function planStages(winningPlan) {
  const stages = [];
  for (let st = winningPlan.queryPlan ?? winningPlan; st; st = st.inputStage ?? (st.inputStages ? st.inputStages[0] : null)) {
    stages.push(st.stage + (st.indexName ? `(${st.indexName})` : ""));
  }
  return stages.join(" > ");
}

function summary(explain) {
  const s = explain.executionStats;
  return {
    plan: planStages(explain.queryPlanner.winningPlan),
    nReturned: s.nReturned,
    keysExamined: s.totalKeysExamined,
    docsExamined: s.totalDocsExamined,
    ms: s.executionTimeMillis
  };
}

// 1) Latest expenses of a user: uses { userId: 1, date: -1 }, no in-memory sort.
print("1) by user, planner choice");
printjson(summary(db.expenses.find({ userId }).sort({ date: -1 }).limit(20).explain("executionStats")));

// 2) Same query forcing a collection scan, as if the index did not exist.
print("2) by user, forced COLLSCAN");
printjson(summary(db.expenses.find({ userId }).sort({ date: -1 }).limit(20).hint({ $natural: 1 }).explain("executionStats")));

// 3) User + date range: the range on "date" is still served by the compound index.
print("3) by user and date range");
printjson(summary(db.expenses.find({
  userId,
  date: { $gte: ISODate("2025-01-01"), $lt: ISODate("2025-04-01") }
}).sort({ date: -1 }).explain("executionStats")));

// 4) Only a date range: the compound index starts with userId, so it cannot be used -> COLLSCAN.
//    (Index prefix rule: { userId, date } serves { userId } and { userId, date }, not { date } alone.)
print("4) date range only");
printjson(summary(db.expenses.find({ date: { $gte: ISODate("2025-01-01"), $lt: ISODate("2025-02-01") } }).explain("executionStats")));

// 5) By category sorted by date: IXSCAN on { categoryId: 1 } + SORT in memory.
print("5) by category sorted by date");
printjson(summary(db.expenses.find({ categoryId }).sort({ date: -1 }).limit(20).explain("executionStats")));

// 6) Aggregations can be explained too: the initial $match uses the index.
print("6) aggregation with $match on userId");
const aggExplain = db.expenses.explain("queryPlanner").aggregate([
  { $match: { userId } },
  { $group: { _id: "$categoryId", total: { $sum: "$amount" } } }
]);
print(aggExplain.queryPlanner ? planStages(aggExplain.queryPlanner.winningPlan) : "multi-stage output, inspect aggExplain.stages");

// Optional: create the index that removes the SORT stage of example 5 and run it again.
//   db.expenses.createIndex({ categoryId: 1, date: -1 }, { name: "ix_expenses_categoryId_date" })
//   db.expenses.dropIndex("ix_expenses_categoryId_date")
