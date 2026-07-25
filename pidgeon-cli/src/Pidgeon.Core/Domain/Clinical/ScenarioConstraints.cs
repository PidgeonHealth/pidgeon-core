// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Clinical.Entities;

namespace Pidgeon.Core.Domain.Clinical;

/// <summary>
/// Demographic and physiological constraints a <see cref="ClinicalScenario"/> imposes on the
/// entity and observation generators. A <c>null</c> member is unconstrained.
///
/// This is the coupling seam between scenario selection and generation: scenarios <em>declare</em>
/// their constraints, entity sampling <em>reads</em> <see cref="RequiredSex"/>/<see cref="AgeYears"/>/
/// <see cref="Pregnant"/> so the patient matches the story, and observation generation <em>reads</em>
/// it so results stay coherent with the patient. It exists so those two generation paths can be
/// coupled to the scenario without sharing files.
/// </summary>
public sealed record ScenarioConstraints
{
    /// <summary>
    /// Biological sex the scenario requires (for example, an O80 pregnancy scenario requires
    /// <see cref="Gender.Female"/>). <c>null</c> leaves sex unconstrained. Maps to
    /// <see cref="Patient.Gender"/> on the sampled entity.
    /// </summary>
    public Gender? RequiredSex { get; init; }

    /// <summary>
    /// Inclusive age band, in years, the scenario requires (for example, a pediatric growth
    /// scenario requires 0–17). <c>null</c> leaves age unconstrained.
    /// </summary>
    public (int Min, int Max)? AgeYears { get; init; }

    /// <summary>
    /// Whether the scenario requires a pregnant patient. When <c>true</c>, this implies
    /// <see cref="RequiredSex"/> is <see cref="Gender.Female"/> and a childbearing age band.
    /// <c>null</c> leaves pregnancy unconstrained.
    /// </summary>
    public bool? Pregnant { get; init; }
}
