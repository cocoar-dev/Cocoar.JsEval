// =============================================================================
// Cocoar.JsEval.Linq — C# LINQ aliases for JavaScript/TypeScript
// =============================================================================
//
// Drop this file into your scripts directory (or add a `/// <reference>` path
// to it) and both the JS-idiomatic names AND the C# LINQ method names will
// be known to TypeScript / Monaco:
//
//   u.Name.includes("x")   // JS-native
//   u.Name.Contains("x")   // C# LINQ alias — emits the same Expression tree
//
//   u.Tags.some(t => ...)  // JS-native
//   u.Tags.Any(t => ...)   // C# LINQ alias
//
// TypeScript's declaration merging will add these signatures alongside the
// built-in lib.es5 ones. At runtime, Cocoar.JsEval.Linq's translator accepts
// both via its method-map and case-insensitive reflection fallback — so both
// produce byte-identical Expression trees.
//
// Note: the `linq.*` typed-literal helpers (linq.guid, linq.decimal, …) and
// the branded primitive types (Guid, Decimal, Long) are emitted automatically
// by `Cocoar.JsEval.TsDefinition` — they're not declared here.
// =============================================================================

interface String {
    /** C# LINQ alias for `includes`. Maps to `string.Contains(string)`. */
    Contains(value: string): boolean;

    /** C# LINQ alias for `startsWith`. Maps to `string.StartsWith(string)`. */
    StartsWith(value: string): boolean;

    /** C# LINQ alias for `endsWith`. Maps to `string.EndsWith(string)`. */
    EndsWith(value: string): boolean;

    /** C# LINQ alias for `indexOf`. Maps to `string.IndexOf(string)` (returns `-1` if not found). */
    IndexOf(value: string): number;

    /** C# LINQ alias for `toLowerCase`. Maps to `string.ToLower()`. */
    ToLower(): string;

    /** C# LINQ alias for `toUpperCase`. Maps to `string.ToUpper()`. */
    ToUpper(): string;

    /** C# LINQ alias for `trim`. Maps to `string.Trim()`. */
    Trim(): string;
}

interface Array<T> {
    /** C# LINQ alias for `some`. Maps to `Enumerable.Any<T>(predicate)`. */
    Any(predicate?: (item: T) => boolean): boolean;

    /** C# LINQ alias for `every`. Maps to `Enumerable.All<T>(predicate)`. */
    All(predicate: (item: T) => boolean): boolean;

    /** C# LINQ alias for `filter`. Maps to `Enumerable.Where<T>(predicate)`. */
    Where(predicate: (item: T) => boolean): T[];

    /** C# LINQ alias for `map`. Maps to `Enumerable.Select<T, R>(selector)`. */
    Select<R>(selector: (item: T) => R): R[];

    /** C# LINQ alias for `find`. Maps to `Enumerable.FirstOrDefault<T>(predicate)`. */
    FirstOrDefault(predicate?: (item: T) => boolean): T | undefined;

    /** C# LINQ alias for `includes`. Maps to `Enumerable.Contains<T>(value)`. */
    Contains(value: T): boolean;
}

interface ReadonlyArray<T> {
    Any(predicate?: (item: T) => boolean): boolean;
    All(predicate: (item: T) => boolean): boolean;
    Where(predicate: (item: T) => boolean): T[];
    Select<R>(selector: (item: T) => R): R[];
    FirstOrDefault(predicate?: (item: T) => boolean): T | undefined;
    Contains(value: T): boolean;
}
