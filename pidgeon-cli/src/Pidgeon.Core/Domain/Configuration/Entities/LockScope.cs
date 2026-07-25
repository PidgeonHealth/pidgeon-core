// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Configuration.Entities;

/// <summary>
/// Defines the scope of values that can be locked in a session.
/// Controls which fields are eligible for locking and consistency management.
/// </summary>
public enum LockScope
{
    /// <summary>
    /// Lock patient-specific values (demographics, identifiers, etc.).
    /// Maintains consistency for a single patient across multiple messages.
    /// </summary>
    Patient = 1,

    /// <summary>
    /// Lock encounter-specific values (visit details, admission info, etc.).
    /// Maintains consistency for a single healthcare encounter or visit.
    /// </summary>
    Encounter = 2,

    /// <summary>
    /// Lock provider-specific values (physician details, ordering provider, etc.).
    /// Maintains consistency for healthcare provider information.
    /// </summary>
    Provider = 3,

    /// <summary>
    /// Lock facility-specific values (hospital details, department codes, etc.).
    /// Maintains consistency for healthcare facility information.
    /// </summary>
    Facility = 4,

    /// <summary>
    /// Lock temporal values (dates, times, sequences).
    /// Maintains chronological consistency across related messages.
    /// </summary>
    Temporal = 5,

    /// <summary>
    /// Lock custom user-defined field patterns.
    /// Allows flexible locking of any specified field paths.
    /// </summary>
    Custom = 6,

    /// <summary>
    /// Lock all compatible fields for maximum consistency.
    /// Creates a comprehensive locked context for complex workflows.
    /// </summary>
    Global = 7
}