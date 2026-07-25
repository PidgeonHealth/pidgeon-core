// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Resolves CE (Coded Element) and CF (Coded Element With Formatted Values) data types
/// with semantic coherence - all 6 components describe the same code.
///
/// Prevents: ^GBR^USA^CHN (random values per component)
/// Ensures: USA^United States^ISO3166 (coherent code + text + system)
///
/// Priority: 82 (between HL7Table at 85 and Demographic at 80)
/// </summary>
public class CodedElementResolver : ICompositeAwareResolver
{
    private readonly ILogger<CodedElementResolver> _logger;
    private readonly IHL7TableProvider _tableProvider;

    public int Priority => 82;

    public CodedElementResolver(
        ILogger<CodedElementResolver> logger,
        IHL7TableProvider tableProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tableProvider = tableProvider ?? throw new ArgumentNullException(nameof(tableProvider));
    }

    // CWE/CNE share CE's identifier^text^coding-system component spine and replaced CE
    // for most coded fields from HL7 v2.5.1 onward — without them, every retyped coded
    // field falls through to component-by-component generation and an ID/PRV placeholder.
    public bool CanHandleComposite(string dataTypeCode)
        => dataTypeCode is "CE" or "CF" or "CWE" or "CNE";

    public async Task<Dictionary<int, string>?> ResolveCompositeAsync(
        SegmentField parentField,
        DataType dataType,
        FieldResolutionContext context)
    {
        try
        {
            // Prefer the field's bound HL7 table when populated (version-aware via the
            // generation context). Shared with ClinicalCodedElementResolver's defer check so
            // both resolvers agree on "is this a constrained HL7 enumeration?".
            var boundTable = await HL7TableBinding.GetPopulatedTableAsync(
                parentField, context.GenerationContext?.TableProvider ?? _tableProvider);
            if (boundTable is not null)
            {
                // Get random value from table
                var tableValue = boundTable.Values[context.FieldRng().Next(boundTable.Values.Count)];

                _logger.LogDebug("Resolved CE composite for {Field} from table {TableId}: {Code}",
                    parentField.Name, parentField.TableId!.Value, tableValue.Code);

                // Build coherent CE components
                return new Dictionary<int, string>
                {
                    { 1, tableValue.Code },                  // CE.1: Identifier (code)
                    { 2, tableValue.Description },           // CE.2: Text (description)
                    { 3, $"HL7{parentField.TableId:D4}" },  // CE.3: Coding system (e.g., "HL70005")
                    { 4, string.Empty },                     // CE.4: Alternate Identifier
                    { 5, string.Empty },                     // CE.5: Alternate Text
                    { 6, string.Empty }                      // CE.6: Alternate Coding System
                };
            }

            // Try semantic patterns based on field name/description
            var fieldName = parentField.Name?.ToLowerInvariant() ?? "";
            var fieldDesc = parentField.Description?.ToLowerInvariant() ?? "";

            // Race codes
            if (fieldName.Contains("race") || fieldDesc.Contains("race"))
            {
                var raceCodes = new[]
                {
                    ("1002-5", "American Indian or Alaska Native", "HL70005"),
                    ("2028-9", "Asian", "HL70005"),
                    ("2054-5", "Black or African American", "HL70005"),
                    ("2076-8", "Native Hawaiian or Other Pacific Islander", "HL70005"),
                    ("2106-3", "White", "HL70005")
                };
                var race = raceCodes[context.FieldRng().Next(raceCodes.Length)];
                return CreateCodedElement(race.Item1, race.Item2, race.Item3);
            }

            // Language codes
            if (fieldName.Contains("language") || fieldDesc.Contains("language"))
            {
                var langCodes = new[]
                {
                    ("en", "English", "ISO639"),
                    ("es", "Spanish", "ISO639"),
                    ("fr", "French", "ISO639"),
                    ("de", "German", "ISO639"),
                    ("zh", "Chinese", "ISO639")
                };
                var lang = langCodes[context.FieldRng().Next(langCodes.Length)];
                return CreateCodedElement(lang.Item1, lang.Item2, lang.Item3);
            }

            // Nationality/Country codes
            if (fieldName.Contains("nationality") || fieldName.Contains("country") ||
                fieldDesc.Contains("nationality") || fieldDesc.Contains("country"))
            {
                var countryCodes = new[]
                {
                    ("USA", "United States", "ISO3166"),
                    ("CAN", "Canada", "ISO3166"),
                    ("GBR", "United Kingdom", "ISO3166"),
                    ("MEX", "Mexico", "ISO3166"),
                    ("CHN", "China", "ISO3166")
                };
                var country = countryCodes[context.FieldRng().Next(countryCodes.Length)];
                return CreateCodedElement(country.Item1, country.Item2, country.Item3);
            }

            // Religion codes
            if (fieldName.Contains("religion") || fieldDesc.Contains("religion"))
            {
                var religionCodes = new[]
                {
                    ("CHR", "Christian", "HL70006"),
                    ("JEW", "Jewish", "HL70006"),
                    ("MOS", "Muslim", "HL70006"),
                    ("BUD", "Buddhist", "HL70006"),
                    ("OTH", "Other", "HL70006")
                };
                var religion = religionCodes[context.FieldRng().Next(religionCodes.Length)];
                return CreateCodedElement(religion.Item1, religion.Item2, religion.Item3);
            }

            // Ethnic group codes
            if (fieldName.Contains("ethnic") || fieldDesc.Contains("ethnic"))
            {
                var ethnicCodes = new[]
                {
                    ("H", "Hispanic or Latino", "HL70189"),
                    ("N", "Not Hispanic or Latino", "HL70189")
                };
                var ethnic = ethnicCodes[context.FieldRng().Next(ethnicCodes.Length)];
                return CreateCodedElement(ethnic.Item1, ethnic.Item2, ethnic.Item3);
            }

            // No match - return null to allow component-by-component generation
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resolving CE composite for field {Field}", parentField.Name);
            return null;
        }
    }

    /// <summary>
    /// Creates a coherent CE/CF component dictionary from code, text, and coding system.
    /// </summary>
    private Dictionary<int, string> CreateCodedElement(string code, string text, string codingSystem)
    {
        return new Dictionary<int, string>
        {
            { 1, code },            // CE.1: Identifier
            { 2, text },            // CE.2: Text
            { 3, codingSystem },    // CE.3: Name Of Coding System
            { 4, string.Empty },    // CE.4: Alternate Identifier
            { 5, string.Empty },    // CE.5: Alternate Text
            { 6, string.Empty }     // CE.6: Alternate Coding System
        };
    }
}
