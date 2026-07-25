// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Interfaces.Semantic;

/// <summary>
/// Maps a medication dosage form to the administration route(s) it can be given by
/// (Semantic Validation dataset #8, openFDA-derived). Enables SEM-F09: an order whose
/// route is incompatible with its formulation — an oral tablet marked intravenous — is
/// detectable because the form's allowed route set is known. Both form values and route
/// values are normalized to canonical tokens before lookup, so the HL7-abbreviated codes
/// the generator emits (TAB, PO, IV) and the openFDA long names (TABLET, ORAL, INTRAVENOUS)
/// resolve to the same rule.
/// </summary>
public interface IRouteFormLoader
{
    /// <summary>
    /// Resolves a raw dosage-form value (RXE-6 / RXO-5) to its canonical form token, or
    /// null when the value matches no known form (an unknown form is never flagged).
    /// </summary>
    string? ResolveForm(string formValue);

    /// <summary>
    /// Resolves a raw route value (RXR-1 / RXE-7) to its canonical route token, or null
    /// when the value matches no known route (an unresolvable route is never flagged).
    /// </summary>
    string? ResolveRoute(string routeValue);

    /// <summary>
    /// The canonical route tokens allowed for the canonical <paramref name="form"/>; empty
    /// when the form carries no rule. Pass a value already returned by <see cref="ResolveForm"/>.
    /// </summary>
    IReadOnlyCollection<string> GetAllowedRoutes(string form);

    /// <summary>The number of form rules loaded; failure when the dataset is unavailable (graceful degradation).</summary>
    Result<int> GetLoadedFormCount();

    /// <summary>
    /// The canonical form tokens that carry a rule; failure when the dataset is unavailable.
    /// The Tracer mutation enumerates these to pick a form whose allowed route set it can
    /// then violate, so the planted (form, route) pair is dataset-derived, never fabricated.
    /// </summary>
    Result<IReadOnlyCollection<string>> GetForms();

    /// <summary>
    /// The union of every canonical route token that appears in any form's allowed set;
    /// failure when the dataset is unavailable. Lets the Tracer pick an incompatible route
    /// that is still a real route the dataset knows (so the check resolves it).
    /// </summary>
    Result<IReadOnlyCollection<string>> GetAllRoutes();
}
