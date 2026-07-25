// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Specialized resolver that can generate semantically coherent composite data types
/// where all components must refer to the same underlying entity/code.
///
/// Examples:
/// - CE (Coded Element): All 6 components describe the same code in different representations
/// - CX (Extended Composite ID): Check digit derives from base ID
/// - XCN (Extended Composite Name): ID and name refer to same person
///
/// This prevents issues like: ^GBR^USA^CHN (different random countries per component)
/// And ensures: USA^United States^ISO3166 (all components coherent)
/// </summary>
public interface ICompositeAwareResolver
{
    /// <summary>
    /// Priority for this resolver in the chain (higher = earlier execution).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Determines if this resolver can handle the specified composite data type.
    /// </summary>
    /// <param name="dataTypeCode">HL7 data type code (e.g., "CE", "CF", "CX")</param>
    /// <returns>True if this resolver handles this composite type</returns>
    bool CanHandleComposite(string dataTypeCode);

    /// <summary>
    /// Resolves all components of a composite data type in a semantically coherent way.
    /// </summary>
    /// <param name="parentField">The field containing this composite</param>
    /// <param name="dataType">The data type definition with component structure</param>
    /// <param name="context">Generation context with clinical entities, options, etc.</param>
    /// <returns>
    /// Dictionary mapping component position (1-based) to component value.
    /// Null return means this resolver cannot handle this specific instance.
    /// </returns>
    Task<Dictionary<int, string>?> ResolveCompositeAsync(
        SegmentField parentField,
        DataType dataType,
        FieldResolutionContext context);
}
