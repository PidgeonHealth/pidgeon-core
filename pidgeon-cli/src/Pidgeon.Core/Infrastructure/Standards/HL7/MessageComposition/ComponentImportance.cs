// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Semantic importance of composite components for realistic data generation.
/// Determines population probability: Critical=95%, Important=50%, Optional=20%.
/// </summary>
public enum ComponentImportance
{
    Critical,   // Core fields needed for realistic data (Street, City, Name)
    Important,  // Commonly populated fields that add realism (Suite, Email, Middle name)
    Optional    // Rarely used fields (Census Tract, Other designation)
}
