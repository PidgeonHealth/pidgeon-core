// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Resolves field values using a specific strategy (session context, HL7 semantics, demographics, etc.).
/// Follows priority-based chain of responsibility pattern for clean separation of concerns.
/// </summary>
public interface IFieldValueResolver
{
    /// <summary>
    /// Resolution priority. Higher values are tried first.
    /// 100: Session context (user-set values)
    /// 90: HL7 specific fields (MSH.1, MSH.2, etc.)
    /// 80: Demographic tables
    /// 70: Clinical context
    /// 10: Smart random fallback
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Attempt to resolve field value using this strategy.
    /// Returns null if this resolver cannot provide a value for this field.
    /// </summary>
    Task<string?> ResolveAsync(FieldResolutionContext context);
}

/// <summary>
/// Context information for field value resolution.
/// Contains all information needed for resolvers to make decisions.
/// </summary>
public record FieldResolutionContext
{
    public string SegmentCode { get; init; } = string.Empty;
    public int FieldPosition { get; init; }
    public SegmentField Field { get; init; } = null!;
    public SegmentGenerationContext GenerationContext { get; init; } = null!;
    public GenerationOptions Options { get; init; } = null!;

    /// <summary>
    /// The coordinate-addressed entropy key for this specific field/component, set by the field
    /// composer from the segment-instance key. Null for contexts built outside the
    /// composer; resolvers should draw via <c>context.FieldRng()</c>, which falls back to the
    /// per-message <see cref="SegmentGenerationContext.Rng"/> when this is unset.
    /// </summary>
    public GenerationKey? FieldKey { get; init; }

    /// <summary>
    /// The composite datatype (e.g. "XTN", "XAD") whose component is currently being resolved,
    /// set by the field composer's component fall-through. Null when resolving a whole field.
    /// Lets keyword resolvers defer on components whose parent semantics they don't understand
    /// (audit D-08: a "street"/"address" keyword must not claim a telecom component).
    /// </summary>
    public string? ParentFieldDataType { get; init; }
}

/// <summary>
/// Service that orchestrates field value resolution using priority-based resolver chain.
/// Tries resolvers in priority order until one returns a value.
/// </summary>
public interface IFieldValueResolverService
{
    /// <summary>
    /// Resolve field value using registered resolvers in priority order.
    /// Guarantees a non-null result (empty string if no resolver succeeds).
    /// </summary>
    Task<string> ResolveFieldValueAsync(FieldResolutionContext context);
}