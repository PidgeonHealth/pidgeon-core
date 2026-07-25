// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Validation;
using Pidgeon.Core.Infrastructure.Standards.Common.HL7;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.Validation;

/// <summary>
/// Owns the per-field HL7 conformance rules: required-field presence
/// (HL7-REQ-001), date/time format (HL7-TS-001, covering TS/DTM/DT/TM), and
/// table-value membership (HL7-TABLE-001). The plugin handles message parsing,
/// segment iteration, and issue aggregation; this collaborator runs the
/// field-level checks for a single segment against its schema. Static format
/// patterns and diagnostic reference data live in
/// <see cref="HL7FieldFormatRules"/>.
/// </summary>
internal sealed class HL7FieldRuleValidator
{
    private readonly IHL7TableProvider _tableProvider;
    private readonly IHL7DataTypeProvider _dataTypeProvider;

    // MSH, FHS, and BHS all declare their own field separator at position 1 (the "|" that never
    // appears in the pipe-split array) and their encoding characters at position 2, so all three
    // use header field numbering: XXX-N maps to Fields[N-1] for N >= 2. BTS/FTS are ordinary
    // trailer segments (no separator field) and use normal numbering.
    private static readonly HashSet<string> HeaderNumberedSegmentCodes =
        new(StringComparer.Ordinal) { "MSH", "FHS", "BHS" };

    public HL7FieldRuleValidator(IHL7TableProvider tableProvider, IHL7DataTypeProvider dataTypeProvider)
    {
        _tableProvider = tableProvider ?? throw new ArgumentNullException(nameof(tableProvider));
        _dataTypeProvider = dataTypeProvider ?? throw new ArgumentNullException(nameof(dataTypeProvider));
    }

    public async Task<(List<ValidationIssue> issues, int rulesChecked, int rulesPassed, int fieldsValidated)>
        ValidateSegmentFieldsAsync(RawHl7Segment segment, SegmentSchema schema, ValidationMode mode)
    {
        var issues = new List<ValidationIssue>();
        int rulesChecked = 0;
        int rulesPassed = 0;
        int fieldsValidated = 0;

        // Header segment has special field numbering:
        //   In the pipe-split array: fields[0]=header code, fields[1]=position 2 (encoding chars),
        //   fields[2]=position 3, ..., fields[N-1]=position N
        //   Position 1 (the "|" separator) does not appear in the split array.
        // For all other segments: fields[0]=code, fields[1]=field.1, fields[N]=field.N
        bool isHeaderSegment = HeaderNumberedSegmentCodes.Contains(segment.Code);

        foreach (var fieldDef in schema.Fields)
        {
            int fieldIndex = isHeaderSegment ? fieldDef.Position - 1 : fieldDef.Position;

            // Skip header position 1 (field separator) — it is the "|" character, never in the array
            if (isHeaderSegment && fieldDef.Position == 1)
            {
                rulesChecked++;
                rulesPassed++;
                fieldsValidated++;
                continue;
            }

            var fieldLocation = $"{segment.Code}.{fieldDef.Position}";
            var fieldValue = fieldIndex < segment.Fields.Length ? segment.Fields[fieldIndex] : "";
            fieldsValidated++;

            rulesChecked++;
            // Known scope gap (audit D-14): only optionality "R" is enforced. Conditional
            // requiredness (C — 93 fields at v2.3 rising to 183 at v2.8) needs the standard's
            // per-field condition predicates, which the embedded oracle does not carry in
            // machine-readable form; B (backward compatibility) and X (not supported) are
            // likewise treated as plain optional. Closing this is an oracle-enrichment
            // project (ADR-0001 scope decision), not a validator patch — do not half-build
            // a C rule here without the condition data.
            bool isRequired = fieldDef.Optionality == "R";
            // Intentionally IsNullOrEmpty — NOT IsNullOrWhiteSpace or a composite-empty check.
            // A bare "^" composite is deliberately NOT "empty" here so it flows past the
            // REQ-001 / continue guards into the type rules; ValidateTimestamp owns the case
            // where a required TS field's first component is blank. Widening this check would
            // collapse that contract and double-report (or silently drop) required "^" fields.
            bool isEmpty = string.IsNullOrEmpty(fieldValue);

            if (isRequired && isEmpty)
            {
                var severity = mode == ValidationMode.Compatibility
                    ? ValidationSeverity.Warning
                    : ValidationSeverity.Error;

                var dataTypeLabel = HL7FieldFormatRules.DataTypeNames.TryGetValue(fieldDef.DataType, out var dtName)
                    ? $"{fieldDef.DataType} ({dtName})"
                    : fieldDef.DataType;

                issues.Add(new ValidationIssue
                {
                    Location = fieldLocation,
                    Severity = severity,
                    Message = $"Required field {fieldLocation} ({fieldDef.Name}) is missing or empty",
                    RuleId = "HL7-REQ-001",
                    ExpectedValue = $"Non-empty value of type {dataTypeLabel}",
                    ActualValue = "(empty)",
                    Suggestion = $"Populate {fieldLocation} with a valid {dataTypeLabel} value"
                });
            }
            else
            {
                rulesPassed++;
            }

            if (isEmpty) continue;

            // HL7-LEN-001 (audit D-12): the oracle's length column bounds the field as
            // transmitted, so the WHOLE raw value (components and all) is graded — never
            // per component. Strict-only and warning-only by product call: real-world
            // feeds exceed published lengths routinely, so overlength is a truncation
            // risk to surface, not spec-invalidity to fail on; compatibility mode stays
            // silent. Length 0 means the oracle publishes no bound — nothing to grade.
            if (mode == ValidationMode.Strict && fieldDef.Length > 0)
            {
                rulesChecked++;
                if (fieldValue.Length > fieldDef.Length)
                {
                    issues.Add(new ValidationIssue
                    {
                        Location = fieldLocation,
                        Severity = ValidationSeverity.Warning,
                        Message = $"Field {fieldLocation} ({fieldDef.Name}) is {fieldValue.Length} characters long, exceeding the maximum length {fieldDef.Length}",
                        RuleId = "HL7-LEN-001",
                        ExpectedValue = $"At most {fieldDef.Length} characters",
                        ActualValue = fieldValue.Length <= 40 ? fieldValue : fieldValue[..40] + "…",
                        Suggestion = $"Shorten {fieldLocation} to {fieldDef.Length} characters or fewer — longer values risk truncation by receiving systems"
                    });
                }
                else
                {
                    rulesPassed++;
                }
            }

            // Repetition handling (audit D-13): a field value may carry multiple
            // repetitions separated by '~'; each repetition is a complete field
            // occurrence, so every per-value rule below grades each repetition
            // individually — grading 'M~F' as the literal code "M~F" both false-flags
            // legal repeats and misnames the real problem when the field cannot repeat
            // at all. MSH-2 is exempt: it DECLARES the encoding characters ('^~\&'),
            // so its literal '~' defines the repetition separator rather than using it.
            bool isEncodingCharsField = isHeaderSegment && fieldDef.Position == 2;
            var repetitions = !isEncodingCharsField && fieldValue.Contains('~')
                ? fieldValue.Split('~')
                : new[] { fieldValue };

            // HL7-REP-001: the oracle marks this field non-repeating ('-'), yet the
            // value carries the repetition separator. Named as its own rule so the
            // diagnosis is "this field does not repeat", not a bogus code/format error.
            if (repetitions.Length > 1 && fieldDef.Repeatability == "-")
            {
                rulesChecked++;
                issues.Add(new ValidationIssue
                {
                    Location = fieldLocation,
                    Severity = mode == ValidationMode.Compatibility
                        ? ValidationSeverity.Warning
                        : ValidationSeverity.Error,
                    Message = $"Field {fieldLocation} ({fieldDef.Name}) does not repeat, but the value contains {repetitions.Length} repetitions",
                    RuleId = "HL7-REP-001",
                    ExpectedValue = "A single occurrence (no '~' repetition separator)",
                    ActualValue = fieldValue,
                    Suggestion = $"Send a single value in {fieldLocation}; the field's repeatability is '-' (no repetition)"
                });
            }

            for (int rep = 0; rep < repetitions.Length; rep++)
            {
                var repetitionValue = repetitions[rep];

                // A trailing/empty repetition ("M~") carries nothing to grade.
                if (repetitions.Length > 1 && string.IsNullOrEmpty(repetitionValue)) continue;

                if (HL7FieldFormatRules.DateTimeRules.TryGetValue(fieldDef.DataType, out var dateTimeRule))
                {
                    rulesChecked++;
                    // The required-but-blank ("^") contract belongs to the FIELD, and the
                    // first repetition satisfies or violates it; later blank-component
                    // repetitions are not independent omissions.
                    var tsIssue = ValidateDateTime(repetitionValue, fieldLocation, mode, isRequired && rep == 0, dateTimeRule);
                    if (tsIssue != null)
                    {
                        issues.Add(tsIssue);
                    }
                    else
                    {
                        rulesPassed++;
                    }
                }

                if (fieldDef.TableId.HasValue && fieldDef.TableId.Value > 0)
                {
                    rulesChecked++;
                    var tableIssue = await ValidateTableValueAsync(repetitionValue, fieldDef, fieldLocation, mode).ConfigureAwait(false);
                    if (tableIssue != null)
                    {
                        issues.Add(tableIssue);
                    }
                    else
                    {
                        rulesPassed++;
                    }
                }
            }
        }

        return (issues, rulesChecked, rulesPassed, fieldsValidated);
    }

    private static ValidationIssue? ValidateDateTime(
        string value, string location, ValidationMode mode, bool isRequired, HL7FieldFormatRules.DateTimeRule rule)
    {
        var tsValue = value.Split('^')[0].Trim();

        // Reached only when fieldValue is a non-empty string whose first component is blank
        // (e.g. a bare "^" composite the generator emits for an unpopulated date/time field) —
        // a fully-absent field is empty, so the caller's REQ-001 / continue guard handles it
        // and it never gets here. For an OPTIONAL field a blank value is nothing to
        // format-check, so skip it; for a REQUIRED field it is a genuine omission REQ-001
        // could not see (its IsNullOrEmpty check treats "^" as present), so flag it here.
        if (string.IsNullOrEmpty(tsValue))
        {
            if (!isRequired)
            {
                return null;
            }

            return new ValidationIssue
            {
                Location = location,
                Severity = mode == ValidationMode.Compatibility
                    ? ValidationSeverity.Warning
                    : ValidationSeverity.Error,
                Message = $"Required {rule.Noun} field {location} is empty",
                RuleId = "HL7-TS-001",
                ExpectedValue = rule.ExpectedFormat,
                ActualValue = "(empty)",
                Suggestion = $"Populate the required {rule.Noun} field with a valid HL7 {rule.Noun}"
            };
        }

        if (!rule.Pattern.IsMatch(tsValue))
        {
            var severity = mode == ValidationMode.Compatibility
                ? ValidationSeverity.Warning
                : ValidationSeverity.Error;

            return new ValidationIssue
            {
                Location = location,
                Severity = severity,
                Message = $"Field {location} has invalid HL7 {rule.Noun} format: '{tsValue}'",
                RuleId = "HL7-TS-001",
                ExpectedValue = rule.ExpectedFormat,
                ActualValue = tsValue,
                Suggestion = $"Use HL7 {rule.Noun} format, e.g., {rule.Example}"
            };
        }

        return null;
    }

    private async Task<ValidationIssue?> ValidateTableValueAsync(
        string fieldValue, SegmentField fieldDef, string location, ValidationMode mode)
    {
        var tableId = fieldDef.TableId!.Value;
        var tableResult = await _tableProvider.GetTableAsync(tableId).ConfigureAwait(false);
        if (!tableResult.IsSuccess)
        {
            return null;
        }

        var table = tableResult.Value!;
        var codeValue = await ResolveTableBoundValueAsync(fieldValue, fieldDef.DataType, tableId).ConfigureAwait(false);

        if (string.IsNullOrEmpty(codeValue)) return null;

        // An empty value set cannot constrain a value, so strict mode must not reject it (nor emit an
        // HL7-TABLE-001 issue with an empty "expected" set). Two cases land here: HL7 user-defined tables
        // (type "User"), whose codes the implementing site defines, and HL7-defined tables this corpus has
        // not yet populated. Both are correct to skip — we can't validate against codes we don't hold.
        if (table.Values.Count == 0) return null;

        // HL7 table codes are case-sensitive ("m" is not the code "M"), so the match is
        // Ordinal. The table side is trimmed symmetrically with the message side because
        // the embedded corpus carries occasional trailing-space data defects (e.g.
        // hl7v24/0277 "Complete ") that would otherwise make the exact wire value
        // permanently unmatchable against its own table.
        bool isValid = table.Values.Any(v =>
            v.Code.Trim().Equals(codeValue, StringComparison.Ordinal)
            || HL7FieldFormatRules.MatchesNumericRange(v.Code, codeValue));

        if (!isValid)
        {
            var severity = ResolveTableSeverity(mode, table, fieldDef);

            var validValuesWithDesc = string.Join(", ", table.Values.Take(10).Select(v =>
                string.IsNullOrEmpty(v.Description) || v.Description == v.Code
                    ? v.Code
                    : $"{v.Code} ({v.Description})"));
            var truncated = table.Values.Count > 10 ? ", ..." : "";

            return new ValidationIssue
            {
                Location = location,
                Severity = severity,
                Message = $"Field {location} value '{codeValue}' is not a valid code in HL7 table {tableId} ({table.Name})",
                RuleId = "HL7-TABLE-001",
                ExpectedValue = $"One of: {validValuesWithDesc}{truncated}",
                ActualValue = codeValue,
                Suggestion = $"Use a valid value from HL7 table {tableId} ({table.Name})"
            };
        }

        return null;
    }

    /// <summary>
    /// Resolves which component of <paramref name="fieldValue"/> the field-level table
    /// actually constrains. A field's schema binds a table to the FIELD, but for composite
    /// datatypes the datatype oracle binds that same table to a specific component (e.g.
    /// PV1-50 is CX with table 0203, which CX carries on CX.5 — Identifier Type Code, not
    /// CX.1 — the identifier itself). Grading component 1 in that case both rejects every
    /// real identifier as "not a valid code" and never checks the component the table
    /// governs. When the datatype resolves and a component other than the first carries
    /// the field's table, grade that component; a first-component match, a primitive
    /// datatype, or an unresolvable datatype keep the historical first-component behavior.
    /// </summary>
    private async Task<string> ResolveTableBoundValueAsync(string fieldValue, string dataTypeCode, int tableId)
    {
        var components = fieldValue.Split('^');

        var dataTypeResult = await _dataTypeProvider.GetDataTypeAsync(dataTypeCode).ConfigureAwait(false);
        if (dataTypeResult is { IsSuccess: true } && dataTypeResult.Value is { } dataType && dataType.Components.Count > 0)
        {
            var boundComponent = dataType.Components.FirstOrDefault(c => c.TableId == tableId);
            if (boundComponent is not null && boundComponent.Position > 1)
            {
                // An unpopulated bound component (field has fewer components) yields empty,
                // which the caller's empty-skip guard treats as nothing to grade.
                return boundComponent.Position <= components.Length
                    ? components[boundComponent.Position - 1].Trim()
                    : string.Empty;
            }
        }

        return components[0].Trim();
    }

    /// <summary>
    /// Strict-mode severity policy for a table mismatch. An HL7-defined table's code set
    /// is normative — a miss is an error. A USER-defined table's published values are
    /// suggestions (the implementing site owns the real set), and a field whose schema
    /// marks its binding "suggested" is likewise advisory — both downgrade to warnings so
    /// strict mode doesn't fail spec-legal site-defined codes (consistent with the silent
    /// skip of EMPTY User tables above). Compatibility mode already warns for everything.
    /// </summary>
    private static ValidationSeverity ResolveTableSeverity(
        ValidationMode mode, TableDefinition table, SegmentField fieldDef)
    {
        if (mode == ValidationMode.Compatibility)
        {
            return ValidationSeverity.Warning;
        }

        if (string.Equals(table.Type, "User", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationSeverity.Warning;
        }

        if (string.Equals(fieldDef.TableBinding, "suggested", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationSeverity.Warning;
        }

        return ValidationSeverity.Error;
    }
}
