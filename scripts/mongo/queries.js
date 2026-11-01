// Handy mongosh queries to explore the data. Paste them into mongosh or the Compass shell.
//   docker exec -it expense-mongo mongosh expense_management

// --- Basic reads -----------------------------------------------------------------
db.users.find().limit(5);
db.expenses.findOne();
db.expenses.countDocuments();
db.expenses.find({ amount: { $gt: Decimal128("1000") } }).sort({ amount: -1 }).limit(5);

// Projection (SELECT only some columns): 1 = include, 0 = exclude
db.users.find({}, { name: 1, email: 1, _id: 0 }).limit(5);

// --- Aggregations used by the API ------------------------------------------------
// Totals per user
db.expenses.aggregate([
  { $group: { _id: "$userId", total: { $sum: "$amount" }, count: { $sum: 1 } } },
  { $sort: { total: -1 } },
  { $lookup: { from: "users", localField: "_id", foreignField: "_id", as: "user" } },
  { $unwind: "$user" },
  { $project: { _id: 0, user: "$user.name", total: 1, count: 1 } }
]);

// Monthly totals for 2025
db.expenses.aggregate([
  { $match: { date: { $gte: ISODate("2025-01-01"), $lt: ISODate("2026-01-01") } } },
  { $group: { _id: { year: { $year: "$date" }, month: { $month: "$date" } }, total: { $sum: "$amount" } } },
  { $sort: { "_id.year": 1, "_id.month": 1 } }
]);

// --- Referential integrity checks (no foreign keys in MongoDB) --------------------
// Expenses whose user does not exist (should be empty)
db.expenses.aggregate([
  { $lookup: { from: "users", localField: "userId", foreignField: "_id", as: "user" } },
  { $match: { user: { $size: 0 } } },
  { $count: "orphans" }
]);

// --- Indexes ---------------------------------------------------------------------
db.expenses.getIndexes();
db.expenses.aggregate([{ $indexStats: {} }, { $project: { name: 1, "accesses.ops": 1 } }]);

// Unique index in action: this fails with E11000 duplicate key error
// db.users.insertOne({ name: "Copy", email: db.users.findOne().email, createdAt: new Date() });

// --- Transactions in mongosh -----------------------------------------------------
// const session = db.getMongo().startSession();
// session.startTransaction();
// const s = session.getDatabase("expense_management");
// s.expenses.deleteMany({ userId: someId });
// s.users.deleteOne({ _id: someId });
// session.commitTransaction();   // or session.abortTransaction()
// session.endSession();
