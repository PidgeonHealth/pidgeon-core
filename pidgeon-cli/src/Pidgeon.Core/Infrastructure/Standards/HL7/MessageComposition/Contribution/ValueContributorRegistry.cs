// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;

/// <summary>
/// Resolves the appropriate <see cref="IValueContributor"/> for a segment code. Returns null when no
/// contributor is registered, so the caller falls back to the schema-driven field pipeline.
/// </summary>
public class ValueContributorRegistry
{
    private readonly IEnumerable<IValueContributor> _contributors;

    public ValueContributorRegistry(IEnumerable<IValueContributor> contributors)
    {
        _contributors = contributors ?? throw new ArgumentNullException(nameof(contributors));
    }

    /// <summary>
    /// Returns the highest-priority contributor that handles <paramref name="segmentCode"/>, or null
    /// if none is registered for that code.
    /// </summary>
    public IValueContributor? GetContributor(string segmentCode)
    {
        return _contributors
            .Where(c => c.SupportedSegments.Contains(segmentCode, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Priority)
            .FirstOrDefault();
    }
}
