// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pidgeon.Core.Domain.DeIdentification;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.DeIdentification;

/// <summary>
/// FHIR R4 de-identification implementation.
/// Walks JSON resources and replaces PHI fields using shared DeIdentificationContext
/// for cross-format consistency.
/// </summary>
public class FHIRDeIdentifier
{
    private readonly FHIRSafeHarborFieldMapper _fieldMapper;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public FHIRDeIdentifier()
    {
        _fieldMapper = new FHIRSafeHarborFieldMapper();
    }

    /// <summary>
    /// De-identifies a FHIR JSON resource or Bundle.
    /// </summary>
    public Result<string> DeIdentifyMessage(string fhirJson, DeIdentificationContext context)
    {
        if (string.IsNullOrWhiteSpace(fhirJson))
            return Result<string>.Failure("FHIR JSON content cannot be null or empty");

        try
        {
            var node = JsonNode.Parse(fhirJson);
            if (node is not JsonObject root)
                return Result<string>.Failure("FHIR content must be a JSON object");

            var resourceType = root["resourceType"]?.GetValue<string>();
            if (string.IsNullOrEmpty(resourceType))
                return Result<string>.Failure("FHIR resource must have a resourceType field");

            DeIdentifyResource(root, resourceType, context);

            return Result<string>.Success(root.ToJsonString(SerializerOptions));
        }
        catch (JsonException ex)
        {
            return Result<string>.Failure($"Invalid FHIR JSON: {ex.Message}");
        }
    }

    private void DeIdentifyResource(JsonObject resource, string resourceType, DeIdentificationContext context)
    {
        if (resourceType == "Bundle")
        {
            DeIdentifyBundle(resource, context);
            return;
        }

        if (!FHIRSafeHarborFieldMapper.GetPhiResourceTypes().Contains(resourceType))
        {
            // Non-PHI resource — only scrub narrative
            ScrubNarrative(resource);
            return;
        }

        DeIdentifyNames(resource, resourceType, context);
        DeIdentifyBirthDate(resource, context);
        DeIdentifyIdentifiers(resource, context);
        DeIdentifyTelecom(resource, context);
        DeIdentifyAddresses(resource, context);
        DeIdentifyContacts(resource, resourceType, context);
        ScrubPhoto(resource);
        ScrubNarrative(resource);
    }

    private void DeIdentifyBundle(JsonObject bundle, DeIdentificationContext context)
    {
        ScrubNarrative(bundle);

        var entries = bundle["entry"]?.AsArray();
        if (entries == null) return;

        foreach (var entry in entries)
        {
            if (entry is not JsonObject entryObj) continue;
            var resource = entryObj["resource"]?.AsObject();
            if (resource == null) continue;

            var resourceType = resource["resourceType"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(resourceType))
            {
                DeIdentifyResource(resource, resourceType, context);
            }
        }
    }

    private void DeIdentifyNames(JsonObject resource, string resourceType, DeIdentificationContext context)
    {
        var names = resource["name"]?.AsArray();
        if (names == null) return;

        var identifierType = resourceType == "Practitioner"
            ? IdentifierType.ProviderName
            : IdentifierType.PatientName;

        foreach (var nameNode in names)
        {
            if (nameNode is not JsonObject name) continue;

            var originalFamily = name["family"]?.GetValue<string>() ?? "";
            var originalGiven = name["given"]?.AsArray()?.FirstOrDefault()?.GetValue<string>() ?? "";
            var originalFull = $"{originalFamily}^{originalGiven}".Trim('^');

            if (string.IsNullOrEmpty(originalFull)) continue;

            var syntheticName = context.GetOrCreateSyntheticId(originalFull, identifierType);
            var (family, given) = SplitSyntheticName(syntheticName);

            name["family"] = family;
            if (name["given"] is JsonArray givenArray && givenArray.Count > 0)
            {
                givenArray[0] = JsonValue.Create(given);
            }
            else
            {
                name["given"] = new JsonArray(JsonValue.Create(given));
            }

            // Clear computed text field
            if (name.ContainsKey("text"))
            {
                name["text"] = $"{given} {family}";
            }

            // Clear prefix/suffix
            if (name.ContainsKey("prefix"))
                name["prefix"] = new JsonArray();
            if (name.ContainsKey("suffix"))
                name["suffix"] = new JsonArray();
        }
    }

    private static void DeIdentifyBirthDate(JsonObject resource, DeIdentificationContext context)
    {
        var birthDateStr = resource["birthDate"]?.GetValue<string>();
        if (string.IsNullOrEmpty(birthDateStr)) return;

        if (DateTime.TryParseExact(birthDateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birthDate))
        {
            var shifted = context.ShiftDate(birthDate, preserveTime: false);
            resource["birthDate"] = shifted.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            context.RecordDateShift(birthDateStr, resource["birthDate"]!.GetValue<string>());
        }
    }

    private static void DeIdentifyIdentifiers(JsonObject resource, DeIdentificationContext context)
    {
        var identifiers = resource["identifier"]?.AsArray();
        if (identifiers == null) return;

        foreach (var idNode in identifiers)
        {
            if (idNode is not JsonObject identifier) continue;

            var value = identifier["value"]?.GetValue<string>();
            if (string.IsNullOrEmpty(value)) continue;

            var idType = DetermineIdentifierType(identifier);
            var syntheticValue = context.GetOrCreateSyntheticId(value, idType);
            identifier["value"] = syntheticValue;
        }
    }

    private static void DeIdentifyTelecom(JsonObject resource, DeIdentificationContext context)
    {
        var telecoms = resource["telecom"]?.AsArray();
        if (telecoms == null) return;

        foreach (var telecomNode in telecoms)
        {
            if (telecomNode is not JsonObject telecom) continue;

            var value = telecom["value"]?.GetValue<string>();
            if (string.IsNullOrEmpty(value)) continue;

            var system = telecom["system"]?.GetValue<string>() ?? "";

            var idType = system switch
            {
                "phone" or "fax" or "pager" or "sms" => IdentifierType.PhoneNumber,
                "email" => IdentifierType.Email,
                _ => IdentifierType.Other
            };

            telecom["value"] = context.GetOrCreateSyntheticId(value, idType);
        }
    }

    private static void DeIdentifyAddresses(JsonObject resource, DeIdentificationContext context)
    {
        var addresses = resource["address"]?.AsArray();
        if (addresses == null) return;

        foreach (var addrNode in addresses)
        {
            if (addrNode is not JsonObject address) continue;

            // Replace street lines
            if (address["line"] is JsonArray lines && lines.Count > 0)
            {
                var originalLine = lines[0]?.GetValue<string>() ?? "";
                if (!string.IsNullOrEmpty(originalLine))
                {
                    var syntheticAddress = context.GetOrCreateSyntheticId(originalLine, IdentifierType.Address);
                    lines[0] = JsonValue.Create(syntheticAddress);
                }
            }

            // Replace city
            var city = address["city"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(city))
            {
                address["city"] = "ANYTOWN";
            }

            // Truncate postal code to first 3 digits + "00"
            var postalCode = address["postalCode"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(postalCode) && postalCode.Length >= 3)
            {
                address["postalCode"] = DeIdentificationContext.RedactZip(postalCode);
            }

            // Preserve state and country for geographic analysis
        }
    }

    private void DeIdentifyContacts(JsonObject resource, string resourceType, DeIdentificationContext context)
    {
        var contacts = resource["contact"]?.AsArray();
        if (contacts == null) return;

        foreach (var contactNode in contacts)
        {
            if (contactNode is not JsonObject contact) continue;

            // De-identify contact name
            if (contact["name"] is JsonObject contactName)
            {
                var originalFamily = contactName["family"]?.GetValue<string>() ?? "";
                var originalGiven = contactName["given"]?.AsArray()?.FirstOrDefault()?.GetValue<string>() ?? "";
                var originalFull = $"{originalFamily}^{originalGiven}".Trim('^');

                if (!string.IsNullOrEmpty(originalFull))
                {
                    var syntheticName = context.GetOrCreateSyntheticId(originalFull, IdentifierType.PatientName);
                    var (family, given) = SplitSyntheticName(syntheticName);
                    contactName["family"] = family;
                    if (contactName["given"] is JsonArray givenArr && givenArr.Count > 0)
                        givenArr[0] = JsonValue.Create(given);
                }
            }

            // De-identify contact telecom
            if (contact["telecom"] is JsonArray contactTelecoms)
            {
                foreach (var telecomNode in contactTelecoms)
                {
                    if (telecomNode is not JsonObject telecom) continue;
                    var value = telecom["value"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(value)) continue;
                    var system = telecom["system"]?.GetValue<string>() ?? "";
                    var idType = system == "email" ? IdentifierType.Email : IdentifierType.PhoneNumber;
                    telecom["value"] = context.GetOrCreateSyntheticId(value, idType);
                }
            }

            // De-identify contact address
            if (contact["address"] is JsonObject contactAddress)
            {
                var addrArray = new JsonArray(contactAddress.Deserialize<JsonNode>()!);
                var tempResource = new JsonObject { ["address"] = addrArray };
                DeIdentifyAddresses(tempResource, context);
            }
        }
    }

    private static void ScrubPhoto(JsonObject resource)
    {
        if (resource.ContainsKey("photo"))
        {
            resource["photo"] = new JsonArray();
        }
    }

    private static void ScrubNarrative(JsonObject resource)
    {
        if (resource["text"] is JsonObject text && text.ContainsKey("div"))
        {
            text["div"] = "<div xmlns=\"http://www.w3.org/1999/xhtml\">De-identified</div>";
        }

        // Annotation.text (note[].text) is free-text narrative that can carry PHI (names, phone numbers,
        // dates in prose) the structured field rules never inspect; redact it whole.
        if (resource["note"] is JsonArray notes)
        {
            foreach (var noteNode in notes)
            {
                if (noteNode is JsonObject note && note.ContainsKey("text"))
                    note["text"] = "De-identified";
            }
        }
    }

    private static IdentifierType DetermineIdentifierType(JsonObject identifier)
    {
        var typeCodings = identifier["type"]?["coding"]?.AsArray();
        if (typeCodings == null) return IdentifierType.Other;

        foreach (var coding in typeCodings)
        {
            if (coding is not JsonObject codingObj) continue;
            var code = codingObj["code"]?.GetValue<string>();

            return code switch
            {
                "MR" => IdentifierType.MedicalRecordNumber,
                "SS" => IdentifierType.SocialSecurityNumber,
                "DL" => IdentifierType.LicenseNumber,
                "AN" => IdentifierType.AccountNumber,
                "MB" or "MA" => IdentifierType.InsuranceId,
                _ => IdentifierType.Other
            };
        }

        return IdentifierType.Other;
    }

    private static (string family, string given) SplitSyntheticName(string syntheticName)
    {
        var parts = syntheticName.Split('^', 2);
        return parts.Length >= 2 ? (parts[0], parts[1]) : (parts[0], "UNKNOWN");
    }
}
