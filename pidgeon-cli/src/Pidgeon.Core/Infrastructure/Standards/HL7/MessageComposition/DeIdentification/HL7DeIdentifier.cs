// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Pidgeon.Core.Application.Services.DeIdentification;
using Pidgeon.Core.Domain.DeIdentification;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.DeIdentification;

/// <summary>
/// HL7 v2.3 specific de-identification implementation.
/// Provides field-aware PHI removal and synthetic replacement for HL7 messages.
/// </summary>
public class HL7DeIdentifier
{
    private readonly SafeHarborFieldMapper _fieldMapper;
    private readonly PhiPatternDetector _patternDetector;
    
    public HL7DeIdentifier()
    {
        _fieldMapper = new SafeHarborFieldMapper();
        _patternDetector = new PhiPatternDetector();
    }

    /// <summary>
    /// De-identifies a complete HL7 v2.3 message by processing each segment.
    /// </summary>
    /// <param name="message">HL7 message to de-identify</param>
    /// <param name="context">De-identification context with mappings</param>
    /// <returns>De-identified HL7 message</returns>
    public Result<string> DeIdentifyMessage(string message, DeIdentificationContext context)
    {
        if (string.IsNullOrWhiteSpace(message))
            return Result<string>.Failure("Message cannot be null or empty");

        try
        {
            var segments = message.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            var deidentifiedSegments = new List<string>();

            foreach (var segment in segments)
            {
                var segmentResult = DeIdentifySegment(segment, context);
                if (segmentResult.IsFailure)
                    return Result<string>.Failure($"Failed to de-identify segment: {segmentResult.Error}");
                
                deidentifiedSegments.Add(segmentResult.Value);
            }

            var deidentifiedMessage = string.Join("\r\n", deidentifiedSegments);
            return Result<string>.Success(deidentifiedMessage);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Error during message de-identification: {ex.Message}");
        }
    }

    /// <summary>
    /// De-identifies a single HL7 segment by processing fields that contain PHI.
    /// </summary>
    /// <param name="segment">HL7 segment to de-identify</param>
    /// <param name="context">De-identification context</param>
    /// <returns>De-identified segment</returns>
    public Result<string> DeIdentifySegment(string segment, DeIdentificationContext context)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return Result<string>.Success(string.Empty);

        try
        {
            var fields = segment.Split('|');
            if (fields.Length == 0)
                return Result<string>.Success(segment);

            var segmentType = fields[0];
            
            // Get PHI field mappings for this segment type
            var phiFields = _fieldMapper.GetPhiFieldsForSegment(segmentType);

            for (int i = 1; i < fields.Length; i++) // Skip segment type (field 0)
            {
                var fieldPath = $"{segmentType}.{i}";
                
                if (phiFields.TryGetValue(fieldPath, out var phiMapping))
                {
                    var fieldResult = DeIdentifyField(fields[i], fieldPath, phiMapping, context);
                    if (fieldResult.IsFailure)
                        return Result<string>.Failure($"Failed to de-identify field {fieldPath}: {fieldResult.Error}");
                    
                    fields[i] = fieldResult.Value;
                    // A FreeText narrative field is scrubbed span-by-span (FreeTextPhiAnalyzer.Scrub records its
                    // own per-span synthetics); its whole value is prose plus synthetics, so recording it whole
                    // would launder any span the scrub missed past the residual-scan compliance verdict.
                    if (phiMapping.IdentifierType != IdentifierType.FreeText)
                        context.RecordEmitted(fields[i]);
                }
                else if (_patternDetector.ContainsPotentialPhi(fields[i]))
                {
                    // Field not in standard mapping but contains potential PHI
                    var fieldResult = DeIdentifyUnmappedField(fields[i], fieldPath, context);
                    if (fieldResult.IsFailure)
                        return Result<string>.Failure($"Failed to de-identify unmapped field {fieldPath}: {fieldResult.Error}");
                    
                    fields[i] = fieldResult.Value;
                    context.RecordEmitted(fields[i]);
                }
            }

            var deidentifiedSegment = string.Join("|", fields);
            return Result<string>.Success(deidentifiedSegment);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Error during segment de-identification: {ex.Message}");
        }
    }

    /// <summary>
    /// De-identifies a specific field based on its PHI mapping.
    /// </summary>
    private Result<string> DeIdentifyField(
        string fieldValue, 
        string fieldPath, 
        PhiFieldMapping mapping, 
        DeIdentificationContext context)
    {
        if (string.IsNullOrWhiteSpace(fieldValue))
            return Result<string>.Success(fieldValue);

        try
        {
            return mapping.IdentifierType switch
            {
                IdentifierType.PatientName => DeIdentifyNameField(fieldValue, context),
                IdentifierType.MedicalRecordNumber => DeIdentifyIdField(fieldValue, IdentifierType.MedicalRecordNumber, context),
                IdentifierType.SocialSecurityNumber => DeIdentifyIdField(fieldValue, IdentifierType.SocialSecurityNumber, context),
                IdentifierType.PhoneNumber => DeIdentifyPhoneField(fieldValue, context),
                IdentifierType.Address => DeIdentifyAddressField(fieldValue, context),
                IdentifierType.Email => DeIdentifyEmailField(fieldValue, context),
                IdentifierType.ProviderName => DeIdentifyNameField(fieldValue, context),
                IdentifierType.AccountNumber => DeIdentifyIdField(fieldValue, IdentifierType.AccountNumber, context),
                IdentifierType.InsuranceId => DeIdentifyIdField(fieldValue, IdentifierType.InsuranceId, context),
                IdentifierType.Date => HL7DateShifter.Shift(fieldValue, context),
                IdentifierType.FreeText => Result<string>.Success(FreeTextPhiAnalyzer.Scrub(fieldValue, context)),
                _ => Result<string>.Success(context.GetOrCreateSyntheticId(fieldValue, mapping.IdentifierType))
            };
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Error de-identifying field {fieldPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// De-identifies fields not in standard mapping but detected as containing PHI.
    /// </summary>
    private Result<string> DeIdentifyUnmappedField(string fieldValue, string fieldPath, DeIdentificationContext context)
    {
        if (string.IsNullOrWhiteSpace(fieldValue))
            return Result<string>.Success(fieldValue);

        var detectedType = _patternDetector.DetectIdentifierType(fieldValue);
        return DeIdentifyField(fieldValue, fieldPath, new PhiFieldMapping
        {
            HipaaCategory = 18, // "Any other unique identifying number, characteristic, or code"
            IdentifierType = detectedType,
            RequiresRemoval = true,
            Description = "Auto-detected PHI"
        }, context);
    }

    /// <summary>
    /// De-identifies name fields preserving HL7 name component structure.
    /// Format: Family^Given^Middle^Suffix^Prefix^Degree
    /// </summary>
    private Result<string> DeIdentifyNameField(string nameField, DeIdentificationContext context)
    {
        if (string.IsNullOrWhiteSpace(nameField))
            return Result<string>.Success(nameField);

        var nameComponents = nameField.Split('^');
        var deidentifiedComponents = new string[nameComponents.Length];

        for (int i = 0; i < nameComponents.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(nameComponents[i]))
            {
                // Generate consistent synthetic names for each component
                var componentKey = $"{nameField}_component_{i}";
                deidentifiedComponents[i] = context.GetOrCreateSyntheticId(componentKey, IdentifierType.PatientName);
                
                // Extract just the name part from the generated format
                if (deidentifiedComponents[i].Contains("^"))
                {
                    var parts = deidentifiedComponents[i].Split('^');
                    deidentifiedComponents[i] = i == 0 ? parts[0] : (parts.Length > 1 ? parts[1] : parts[0]);
                }
            }
            else
            {
                deidentifiedComponents[i] = nameComponents[i];
            }
        }

        return Result<string>.Success(string.Join("^", deidentifiedComponents));
    }

    /// <summary>
    /// De-identifies identifier fields (MRN, SSN, Account Numbers, etc.).
    /// </summary>
    private Result<string> DeIdentifyIdField(string idField, IdentifierType identifierType, DeIdentificationContext context)
    {
        if (string.IsNullOrWhiteSpace(idField))
            return Result<string>.Success(idField);

        // Handle composite identifiers (ID^AssigningAuthority^IdentifierType)
        var idComponents = idField.Split('^');
        if (idComponents.Length > 1)
        {
            // Replace the ID part but preserve assigning authority structure
            var syntheticId = context.GetOrCreateSyntheticId(idComponents[0], identifierType);
            idComponents[0] = syntheticId;
            
            // Also de-identify assigning authority if it contains identifying information
            if (idComponents.Length > 1 && !string.IsNullOrWhiteSpace(idComponents[1]))
            {
                idComponents[1] = "PIDGEON_SYNTHETIC";
            }
            
            return Result<string>.Success(string.Join("^", idComponents));
        }
        else
        {
            // Simple identifier
            var syntheticId = context.GetOrCreateSyntheticId(idField, identifierType);
            return Result<string>.Success(syntheticId);
        }
    }

    /// <summary>
    /// De-identifies phone number fields preserving format.
    /// </summary>
    private Result<string> DeIdentifyPhoneField(string phoneField, DeIdentificationContext context)
    {
        if (string.IsNullOrWhiteSpace(phoneField))
            return Result<string>.Success(phoneField);

        var syntheticPhone = context.GetOrCreateSyntheticId(phoneField, IdentifierType.PhoneNumber);
        return Result<string>.Success(syntheticPhone);
    }

    /// <summary>
    /// De-identifies address fields preserving HL7 address component structure.
    /// Format: StreetAddress^OtherDesignation^City^State^ZIP^Country^AddressType^OtherGeographicDesignation
    /// </summary>
    private Result<string> DeIdentifyAddressField(string addressField, DeIdentificationContext context)
    {
        if (string.IsNullOrWhiteSpace(addressField))
            return Result<string>.Success(addressField);

        var addressComponents = addressField.Split('^');
        var deidentifiedComponents = new string[addressComponents.Length];

        for (int i = 0; i < addressComponents.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(addressComponents[i]))
            {
                if (i == 0) // Street address
                {
                    deidentifiedComponents[i] = context.GetOrCreateSyntheticId(addressComponents[i], IdentifierType.Address);
                }
                else if (i == 2) // City
                {
                    deidentifiedComponents[i] = "ANYCITY";
                }
                else if (i == 4) // ZIP - follow HIPAA rules for ZIP codes
                {
                    var zip = addressComponents[i];
                    if (zip.Length >= 3)
                    {
                        // Keep first 3 digits if population > 20,000 (simplified - using common ZIP)
                        deidentifiedComponents[i] = DeIdentificationContext.RedactZip(zip);
                    }
                    else
                    {
                        deidentifiedComponents[i] = "00000";
                    }
                }
                else
                {
                    // Preserve state, country, address type
                    deidentifiedComponents[i] = addressComponents[i];
                }
            }
            else
            {
                deidentifiedComponents[i] = addressComponents[i];
            }
        }

        return Result<string>.Success(string.Join("^", deidentifiedComponents));
    }

    /// <summary>
    /// De-identifies email addresses while preserving domain structure when possible.
    /// </summary>
    private Result<string> DeIdentifyEmailField(string emailField, DeIdentificationContext context)
    {
        if (string.IsNullOrWhiteSpace(emailField))
            return Result<string>.Success(emailField);

        var syntheticEmail = context.GetOrCreateSyntheticId(emailField, IdentifierType.Email);
        return Result<string>.Success(syntheticEmail);
    }
}
