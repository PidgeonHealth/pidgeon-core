// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Classifies a composite component's semantic importance: the tier that drives its
/// population probability when generating realistic data. Separates field-shaping
/// heuristics from message orchestration. A pure function of the component and its parent
/// field; holds no generation state.
/// </summary>
public interface IComponentImportanceClassifier
{
    /// <summary>
    /// Maps a composite component (by name, position, and parent field data type:
    /// XAD / XPN / CX / XTN / CE / CWE) to its importance tier — Critical (95% populated),
    /// Important (50%), or Optional (20%).
    /// </summary>
    ComponentImportance GetComponentImportance(DataTypeComponent component, SegmentField parentField);
}
