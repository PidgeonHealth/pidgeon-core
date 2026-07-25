// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Interfaces.Reference;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Default <see cref="IHL7TableValueSource"/>: random value selection from HL7 coded-value
/// tables and demographic tables. The demographic table JSON parse + list materialization
/// happen once per table in <see cref="IDemographicTableProvider"/> (Singleton, cached).
/// Each draw reads its table's coordinate off the segment key, so output is reproducible
/// for a given seed.
/// </summary>
public class HL7TableValueSource : IHL7TableValueSource
{
    private readonly IDemographicTableProvider _demographicTableProvider;

    public HL7TableValueSource(IDemographicTableProvider demographicTableProvider)
    {
        _demographicTableProvider = demographicTableProvider ?? throw new ArgumentNullException(nameof(demographicTableProvider));
    }

    public async Task<string> GenerateValueFromTableAsync(int tableId, IHL7TableProvider tableProvider, SegmentGenerationContext context)
    {
        var tableResult = await tableProvider.GetTableAsync(tableId);
        if (tableResult.IsFailure)
        {
            return string.Empty;
        }

        // Template-notation codes (Q<integer>J<day#>, TH<integer>, <null>) are spec notation,
        // not sendable values (audit D-06); a templates-only table behaves as empty.
        var values = HL7TableCodeTemplates.Selectable(tableResult.Value.Values);
        if (values.Count == 0)
        {
            return string.Empty;
        }

        var randomValue = values[
            context.Key.Derive("table").Derive(tableId).AsRandom().Next(values.Count)];
        return randomValue.Code;
    }

    public Task<string?> GenerateValueFromDemographicTableAsync(string tableName, SegmentGenerationContext context)
    {
        var values = _demographicTableProvider.GetValues(tableName);
        var result = values.Count > 0
            ? values[context.Key.Derive("demographic-table").Derive(tableName).AsRandom().Next(values.Count)]
            : null;
        return Task.FromResult(result);
    }
}
