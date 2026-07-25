// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using Pidgeon.Core.Application.Interfaces.Conformance;
using Pidgeon.Core.Domain.Conformance;

namespace Pidgeon.Core.Application.Services.Conformance;

/// <summary>
/// Routes content to the first registered <see cref="IIndependentOracle"/> that can validate
/// the target's standard. When no oracle applies, reports
/// <see cref="ConformanceLevel.NotEvaluated"/> rather than silently passing.
/// </summary>
public sealed class VersionedConformanceService : IVersionedConformanceService
{
    private readonly IReadOnlyList<IIndependentOracle> _oracles;

    public VersionedConformanceService(IEnumerable<IIndependentOracle> oracles)
    {
        _oracles = oracles.ToList();
    }

    public VersionedConformanceResult Check(string content, ConformanceTarget target)
    {
        var oracle = _oracles.FirstOrDefault(o => o.CanValidate(target));
        if (oracle is null)
        {
            return new VersionedConformanceResult(
                target,
                ConformanceLevel.NotEvaluated,
                new List<ConformanceFinding>());
        }

        return oracle.Validate(content, target);
    }
}
