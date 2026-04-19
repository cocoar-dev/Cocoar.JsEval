// =============================================================================
// Cocoar.JsEval.Linq — `linq` runtime global (typed-literal helpers)
// =============================================================================
//
// Emitted automatically by Cocoar.JsEval.TsDefinition when AddLinq() is called
// on the JsEvalBuilder. Declares the `linq.*` helpers that produce precision-
// preserving .NET primitives inside LINQ predicates.
//
// Usage (inside a predicate translated by JsExpressionTranslator):
//   users.where(u => u.Price      > linq.decimal('99.99'))
//   users.where(u => u.ExternalId === linq.long('9007199254740993'))
//   todos.where(t => t.CreatedAt < linq.today())
//
// Precision is guaranteed at translation time: the translator recognises
// `linq.decimal('…')` / `linq.long('…')` / `linq.guid('…')` / `linq.date(…)` /
// etc. as AST patterns and emits a typed ConstantExpression directly — Jint's
// runtime never sees the value, so IEEE-754 rounding can't lose digits. The
// runtime `linq` object exists so plain JS scripts don't break with
// `ReferenceError: linq is not defined` when evaluated outside a translator
// context; there the helpers fall back to plain parsers.
//
// The branded types (Guid, Decimal, Long) come from `cocoar-jseval-linq.d.ts`
// via declaration merging.
// =============================================================================

declare const linq: {
    /** Parse as System.Decimal — precision preserved at translation time. */
    decimal(value: string): Decimal;

    /** Parse as System.Double (IEEE-754 double). */
    double(value: string): number;

    /** Parse as System.Int32. */
    int(value: string): number;

    /** Parse as System.Int64 — bigint range preserved at translation time. */
    long(value: string): Long;

    /** Parse as System.DateTime (unspecified kind). */
    date(value: string): Date;

    /** Parse as System.DateTime with Utc kind. */
    dateUtc(value: string): Date;

    /** Parse as System.DateTimeOffset. */
    dateOffset(value: string): Date;

    /** Parse as System.DateOnly (date portion only). */
    dateOnly(value: string): Date;

    /** Parse as System.TimeOnly. */
    timeOnly(value: string): Date;

    /** Parse as System.TimeSpan (total, in a unit the caller picks). */
    timeSpan(value: string): number;

    /** Parse as System.Guid. */
    guid(value: string): Guid;

    /** Capture today's local date at translation time. */
    today(): Date;

    /** Capture now (local time) at translation time. */
    now(): Date;

    /** Capture now (UTC) at translation time. */
    utcNow(): Date;

    /** Capture today's UTC date at translation time. */
    todayUtc(): Date;
};
