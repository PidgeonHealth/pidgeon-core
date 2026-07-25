// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Generation.Types;

/// <summary>
/// Types of patients affecting generation patterns and clinical appropriateness.
/// Each type influences age ranges, medication selection, and clinical correlations.
/// </summary>
public enum PatientType
{
    /// <summary>
    /// General adult population (18-64 years).
    /// Standard medication dosing and common adult conditions.
    /// </summary>
    General,

    /// <summary>
    /// Pediatric patients (0-17 years) with age-appropriate medications and dosing.
    /// Weight-based dosing, pediatric-specific medications, vaccination schedules.
    /// </summary>
    Pediatric,

    /// <summary>
    /// Geriatric patients (65+ years) with polypharmacy and age-related conditions.
    /// Multiple chronic conditions, drug interactions, reduced dosing considerations.
    /// </summary>
    Geriatric,

    /// <summary>
    /// Correctional healthcare patients with specific security and workflow requirements.
    /// Controlled substance restrictions, security protocols, limited formulary.
    /// </summary>
    Correctional,

    /// <summary>
    /// Emergency department patients with acute conditions and rapid workflow needs.
    /// Acute medications, rapid administration, minimal medical history.
    /// </summary>
    EmergencyDepartment,

    /// <summary>
    /// Long-term care patients with chronic conditions and medication management focus.
    /// Complex medication regimens, multiple comorbidities, care coordination.
    /// </summary>
    LongTermCare
}