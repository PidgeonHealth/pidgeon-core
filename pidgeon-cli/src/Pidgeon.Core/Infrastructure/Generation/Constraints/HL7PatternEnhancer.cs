// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Configuration;

namespace Pidgeon.Core.Infrastructure.Generation.Constraints;

/// <summary>
/// Applies well-known HL7 v2.3 data-type patterns (<c>IS</c>, <c>ST</c>,
/// <c>NM</c>, <c>DT</c>, <c>TS</c>) to a raw <see cref="FieldConstraints"/>
/// record. Owns the per-data-type enhancement rules.
/// </summary>
public sealed class HL7PatternEnhancer
{
    /// <summary>
    /// Returns a new <see cref="FieldConstraints"/> augmented with HL7
    /// data-type-specific patterns (regex, numeric ranges, datetime bounds,
    /// default max length). Leaves unknown or unset data types untouched.
    /// </summary>
    public FieldConstraints Enhance(FieldConstraints constraints, int fieldPosition)
    {
        _ = fieldPosition; // reserved for future position-aware tweaks

        return constraints.DataType?.ToUpperInvariant() switch
        {
            "IS" when constraints.TableReference != null => constraints with
            {
                MaxLength = 1
            },
            "ST" => constraints with
            {
                MaxLength = constraints.MaxLength ?? 200
            },
            "NM" => constraints with
            {
                Pattern = @"^\d+(\.\d+)?$",
                Numeric = new NumericConstraints(0, 999999, null, null)
            },
            "DT" => constraints with
            {
                Pattern = @"^\d{8}$",
                DateTime = new DateTimeConstraints(
                    System.DateTime.Today.AddYears(-120),
                    System.DateTime.Today.AddYears(10),
                    "yyyyMMdd")
            },
            "TS" => constraints with
            {
                Pattern = @"^\d{8,14}$",
                DateTime = new DateTimeConstraints(
                    System.DateTime.Today.AddYears(-120),
                    System.DateTime.Today.AddYears(10),
                    "yyyyMMddHHmmss")
            },
            _ => constraints
        };
    }
}
