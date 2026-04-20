using System.Collections.Generic;

namespace Cocoar.JsEval;

/// <summary>
/// Extension point for packages that want to contribute additional
/// <c>.d.ts</c> files to <c>TsDefinitionService.GetTsDefinitions()</c>
/// without being registered as a <see cref="IJsModule"/>.
///
/// <para>
/// Canonical use: <c>Cocoar.JsEval.Linq</c> sets up a runtime <c>linq</c>
/// global via <c>engine.Execute(...)</c> in <c>LinqCasts.Register</c> — that
/// global can't be derived from a module type via reflection, so the Linq
/// package ships a hand-written <c>linq.d.ts</c> and contributes it through
/// this interface. Any third-party package with its own runtime-only
/// globals / aliases can do the same.
/// </para>
///
/// <para>
/// Contributors are resolved from the DI container by
/// <c>TsDefinitionService</c>; every implementation that's registered as
/// <c>IJsTsDefinitionContributor</c> is invoked and its entries merged into
/// the returned definitions dictionary. Contributor entries win over the
/// package's built-ins when keys collide, and later contributors win over
/// earlier ones — so hosts that need to override a bundled file can.
/// </para>
/// </summary>
public interface IJsTsDefinitionContributor
{
    /// <summary>
    /// Returns file-name / content pairs to merge into
    /// <c>TsDefinitionService.GetTsDefinitions()</c>.
    /// Called once per <c>GetTsDefinitions()</c> invocation.
    /// </summary>
    IEnumerable<KeyValuePair<string, string>> GetTsDefinitions();
}
