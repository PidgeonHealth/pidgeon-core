// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Application.Interfaces.Standards;
using Pidgeon.Core.Application.DTOs;
using Pidgeon.Core.Application.Services.Clinical;
using Pidgeon.Core.Generation;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Application.Services.Generation.Plugins;

/// <summary>
/// HL7-specific message generation plugin using hybrid approach.
/// Combines rich healthcare context with clean workflow-based organization.
/// </summary>
internal class HL7MessageGenerationPlugin : IMessageGenerationPlugin
{
    private readonly Pidgeon.Core.Generation.IGenerationService _domainGenerationService;
    private readonly IStandardPluginRegistry _pluginRegistry;
    private readonly IHL7MessageFactory _hl7MessageFactory;
    private readonly ClinicalScenarioCoordinator? _scenarioCoordinator;

    public string StandardName => "hl7";

    public HL7MessageGenerationPlugin(
        Pidgeon.Core.Generation.IGenerationService domainGenerationService,
        IStandardPluginRegistry pluginRegistry,
        IHL7MessageFactory hl7MessageFactory,
        ClinicalScenarioCoordinator? scenarioCoordinator = null)
    {
        _domainGenerationService = domainGenerationService;
        _pluginRegistry = pluginRegistry;
        _hl7MessageFactory = hl7MessageFactory;
        _scenarioCoordinator = scenarioCoordinator;
    }

    // The generatable message types are listed in HL7GeneratableMessageTypes.

    public bool CanHandleMessageType(string messageType)
    {
        if (string.IsNullOrWhiteSpace(messageType))
            return false;

        // Check exact match first
        if (HL7GeneratableMessageTypes.All.Contains(messageType))
            return true;

        // Check base type (ADT^A01 -> ADT)
        var baseType = ExtractBaseMessageType(messageType);
        return HL7GeneratableMessageTypes.All.Contains(baseType);
    }

    public async Task<Result<IReadOnlyList<string>>> GenerateMessagesAsync(string messageType, int count, GenerationOptions? options = null)
    {
        try
        {
            var messages = new List<string>();
            var generationOptions = options ?? new GenerationOptions();

            // Clinical content is addressed by coordinate, not RNG draw order: the scenario
            // stream shares the composer's seeded root, narrowed per message index in the loop below.
            var scenarioRoot = GenerationDeterminism.CreateKey(generationOptions).Derive(messageType).Derive("scenario");

            for (int i = 0; i < count; i++)
            {
                // Each message's clinical content is keyed to its index so a fixed-seed batch varies per
                // message yet stays reproducible and invariant to draw order. Non-cohort messages get a
                // fresh scenario each; a cohort keeps its shared scenario and patient across the batch.
                if (!generationOptions.IsCohortSequence)
                    _scenarioCoordinator?.ClearScenario();
                _scenarioCoordinator?.ApplySeed(scenarioRoot.Derive(i));
                PinScenarioCondition(generationOptions);

                // The index is the message-instance coordinate (ADR-0005 §3): CreateKey folds it
                // into the entropy root, so each message draws its own patient, MSH-10 control id,
                // and field values. Without it the batch collapses onto one coordinate — one
                // patient, one control id, only the separately-indexed scenario varying. Cohorts
                // still share their patient via the coordinator's CohortPatient cache, not via
                // key collision, so indexing does not disturb cohort demographics.
                var messageOptions = generationOptions with { MessageIndex = i };

                var result = await GenerateSingleMessageAsync(messageType, messageOptions);
                
                if (!result.IsSuccess)
                    return Result<IReadOnlyList<string>>.Failure(result.Error);
                    
                messages.Add(result.Value);
            }
            
            return Result<IReadOnlyList<string>>.Success(messages);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<string>>.Failure($"HL7 message generation failed: {ex.Message}");
        }
    }

    public IReadOnlyList<string> GetSupportedMessageTypes()
    {
        return HL7GeneratableMessageTypes.All.OrderBy(x => x).ToList();
    }

    public string GetUnsupportedMessageTypeError(string messageType)
    {
        var baseType = ExtractBaseMessageType(messageType);
        var suggestions = new List<string>();

        // Check for common HL7 patterns and provide intelligent suggestions
        if (messageType.Contains("^"))
        {
            var supportedForBase = HL7GeneratableMessageTypes.All.Where(mt => mt.StartsWith($"{baseType}^", StringComparison.OrdinalIgnoreCase))
                                                .Take(3)
                                                .ToList();
            
            if (supportedForBase.Any())
                suggestions.Add($"HL7 supports these {baseType} messages: {string.Join(", ", supportedForBase)}");
        }
        else
        {
            // Common user mistakes with helpful HL7-specific guidance
            var commonSuggestions = messageType.ToLowerInvariant() switch
            {
                "patient" => "HL7 doesn't have 'patient' messages. Use ADT^A01 (Admission), ADT^A08 (Update), or ADT^A03 (Discharge)",
                "prescription" or "medication" => "HL7 uses RDE^O11 (Pharmacy Order) or RGV^O15 (Medication Administration) for prescriptions",
                "lab" or "labs" or "results" => "HL7 uses ORU^R01 (Lab Results) or ORU^R03 (Display Results) for laboratory data",
                "order" or "orders" => "HL7 uses ORM^O01 (General Order) for clinical orders",
                "appointment" => "HL7 uses SIU^S12 (New Appointment) or SIU^S13 (Reschedule) for scheduling",
                "document" => "HL7 uses MDM^T02 (New Document) or MDM^T04 (Document Edit) for clinical documents",
                _ => null
            };

            if (commonSuggestions != null)
                suggestions.Add(commonSuggestions);
        }

        var errorMessage = $"HL7 standard doesn't support message type: {messageType}";
        if (suggestions.Any())
        {
            errorMessage += "\n\nSuggestions:\n" + string.Join("\n", suggestions.Select(s => $"  • {s}"));
        }
        else
        {
            var commonTypes = HL7GeneratableMessageTypes.All.Where(mt => mt.Contains("^"))
                                          .Take(5)
                                          .ToList();
            errorMessage += $"\n\nCommon HL7 message types:\n  • {string.Join("\n  • ", commonTypes)}";
        }

        return errorMessage;
    }

    /// <summary>
    /// Central routing logic - delegates to workflow-specific generation methods.
    /// </summary>
    private async Task<Result<string>> GenerateSingleMessageAsync(string messageType, GenerationOptions options)
    {
        var baseType = ExtractBaseMessageType(messageType);

        // Couple the patient to the story: consult the message's clinical scenario BEFORE the entity
        // is sampled, so a pregnancy scenario lands on a female of childbearing age and a sexed/aged
        // diagnosis gets a matching patient (GENERATION_COHERENCE_WAVE lane B). Reading the scenario
        // here memoizes the same weighted selection the DG1/OBX contributors reuse — clinical content
        // is unchanged; only the demographics couple. A null coordinator leaves the patient a free draw.
        var scenarioConstraints = _scenarioCoordinator?.GetCurrentScenario()?.Constraints;
        if (scenarioConstraints is not null)
            options = options with { ScenarioConstraints = scenarioConstraints };

        return baseType.ToUpperInvariant() switch
        {
            "ADT" => await GenerateAdmitDischargeTransferAsync(messageType, options),
            "ORM" => await GenerateOrderManagementAsync(messageType, options),
            "ORU" => await GenerateObservationMessageAsync(messageType, options),
            "RDE" => await GeneratePharmacyOrderAsync(messageType, options),
            "RGV" => await GeneratePharmacyGiveAsync(messageType, options),
            "RAS" => await GeneratePharmacyAdministrationAsync(messageType, options),
            "SIU" => await GenerateSchedulingAsync(messageType, options),
            "MDM" => await GenerateMedicalDocumentAsync(messageType, options),
            "QBP" => await GenerateQueryAsync(messageType, options),
            "RSP" => await GenerateQueryResponseAsync(messageType, options),
            "ACK" => await GenerateAcknowledgmentAsync(messageType, options),
            "BAR" => await GenerateFinancialMessageAsync(messageType, options),
            "DFT" => await GenerateFinancialMessageAsync(messageType, options),
            "OML" => await GenerateObservationMessageAsync(messageType, options),
            "VXU" => await GenerateImmunizationAsync(messageType, options),
            _ => Result<string>.Failure(GetUnsupportedMessageTypeError(messageType))
        };
    }

    // === Clinical Workflow Generation Methods ===

    /// <summary>
    /// ADT - Admit/Discharge/Transfer workflow
    /// Handles patient movement throughout healthcare facility.
    /// </summary>
    private async Task<Result<string>> GenerateAdmitDischargeTransferAsync(string messageType, GenerationOptions options)
    {
        await Task.Yield();

        // When running a cohort sequence, reuse an already-cached patient so all
        // messages share the same demographics (PID-3 MRN, PID-5 name, PID-19 SSN).
        Patient? patient = options.IsCohortSequence ? _scenarioCoordinator?.CohortPatient : null;

        if (patient == null)
        {
            var encounterResult = _domainGenerationService.GenerateEncounter(options);
            if (!encounterResult.IsSuccess)
                return Result<string>.Failure(encounterResult.Error);

            var encounter = encounterResult.Value;
            patient = encounter.Patient ?? _domainGenerationService.GeneratePatient(options).Value;

            if (patient == null)
                return Result<string>.Failure("Patient generation failed for ADT message");

            // Cache for subsequent messages in the cohort
            if (options.IsCohortSequence && _scenarioCoordinator != null)
                _scenarioCoordinator.CohortPatient = patient;

            return await _hl7MessageFactory.GenerateMessageAsync(messageType, patient, encounter, null, null, null, options);
        }

        return await _hl7MessageFactory.GenerateMessageAsync(messageType, patient, null, null, null, null, options);
    }

    /// <summary>
    /// ORM - Order Management workflow  
    /// Handles clinical orders (lab tests, procedures, medications).
    /// </summary>
    private async Task<Result<string>> GenerateOrderManagementAsync(string messageType, GenerationOptions options)
    {
        await Task.Yield();

        // Reuse cohort patient to maintain cross-message demographic consistency
        Patient? cohortPatient = options.IsCohortSequence ? _scenarioCoordinator?.CohortPatient : null;

        var prescriptionResult = _domainGenerationService.GeneratePrescription(options);
        if (!prescriptionResult.IsSuccess)
            return Result<string>.Failure(prescriptionResult.Error);

        var prescription = prescriptionResult.Value;

        // Override the patient with the cohort patient if available
        var patient = cohortPatient ?? prescription.Patient;

        if (options.IsCohortSequence && cohortPatient == null && _scenarioCoordinator != null)
            _scenarioCoordinator.CohortPatient = patient;

        return await _hl7MessageFactory.GenerateMessageAsync(messageType, patient, null, prescription, null, null, options);
    }

    /// <summary>
    /// ORU / OML - Observation and laboratory-order workflow.
    /// Handles observation results (ORU) and laboratory orders (OML); both share the
    /// patient + observation domain setup and the ORC/OBR/observation segment spine.
    /// </summary>
    private async Task<Result<string>> GenerateObservationMessageAsync(string messageType, GenerationOptions options)
    {
        await Task.Yield();

        // Reuse cohort patient to maintain cross-message demographic consistency
        Patient? cohortPatient = options.IsCohortSequence ? _scenarioCoordinator?.CohortPatient : null;

        var patientResult = _domainGenerationService.GeneratePatient(options);
        if (!patientResult.IsSuccess)
            return Result<string>.Failure(patientResult.Error);

        var patient = cohortPatient ?? patientResult.Value;

        if (options.IsCohortSequence && cohortPatient == null && _scenarioCoordinator != null)
            _scenarioCoordinator.CohortPatient = patient;

        // Generate realistic observation result using service
        var observationResult = _domainGenerationService.GenerateObservationResult(options);
        if (!observationResult.IsSuccess)
            return Result<string>.Failure(observationResult.Error);

        var observation = observationResult.Value;

        return await _hl7MessageFactory.GenerateMessageAsync(messageType, patient, null, null, observation, null, options);
    }

    /// <summary>
    /// RDE - Pharmacy Order workflow
    /// Handles detailed pharmacy orders with dosing instructions.
    /// </summary>
    private async Task<Result<string>> GeneratePharmacyOrderAsync(string messageType, GenerationOptions options)
    {
        await Task.Yield();

        // Reuse cohort patient to maintain cross-message demographic consistency.
        Patient? cohortPatient = options.IsCohortSequence ? _scenarioCoordinator?.CohortPatient : null;

        var prescriptionResult = _domainGenerationService.GeneratePrescription(options);
        if (!prescriptionResult.IsSuccess)
            return Result<string>.Failure(prescriptionResult.Error);

        var prescription = prescriptionResult.Value;
        var patient = cohortPatient ?? prescription.Patient;

        if (options.IsCohortSequence && cohortPatient == null && _scenarioCoordinator != null)
            _scenarioCoordinator.CohortPatient = patient;

        return await _hl7MessageFactory.GenerateMessageAsync(messageType, patient, null, prescription, null, null, options);
    }

    /// <summary>
    /// RGV - Pharmacy Give workflow
    /// Records medication administration to patients.
    /// </summary>
    private async Task<Result<string>> GeneratePharmacyGiveAsync(string messageType, GenerationOptions options)
    {
        await Task.Yield();

        // Reuse cohort patient to maintain cross-message demographic consistency.
        Patient? cohortPatient = options.IsCohortSequence ? _scenarioCoordinator?.CohortPatient : null;

        var prescriptionResult = _domainGenerationService.GeneratePrescription(options);
        if (!prescriptionResult.IsSuccess)
            return Result<string>.Failure(prescriptionResult.Error);

        var prescription = prescriptionResult.Value;
        var patient = cohortPatient ?? prescription.Patient;

        if (options.IsCohortSequence && cohortPatient == null && _scenarioCoordinator != null)
            _scenarioCoordinator.CohortPatient = patient;

        return await _hl7MessageFactory.GenerateMessageAsync(messageType, patient, null, prescription, null, null, options);
    }

    /// <summary>
    /// RAS - Pharmacy Administration workflow
    /// Handles pharmacy administration status updates.
    /// </summary>
    private async Task<Result<string>> GeneratePharmacyAdministrationAsync(string messageType, GenerationOptions options)
    {
        await Task.Yield();

        // Reuse cohort patient to maintain cross-message demographic consistency.
        Patient? cohortPatient = options.IsCohortSequence ? _scenarioCoordinator?.CohortPatient : null;

        var prescriptionResult = _domainGenerationService.GeneratePrescription(options);
        if (!prescriptionResult.IsSuccess)
            return Result<string>.Failure(prescriptionResult.Error);

        var prescription = prescriptionResult.Value;
        var patient = cohortPatient ?? prescription.Patient;

        if (options.IsCohortSequence && cohortPatient == null && _scenarioCoordinator != null)
            _scenarioCoordinator.CohortPatient = patient;

        return await _hl7MessageFactory.GenerateMessageAsync(messageType, patient, null, prescription, null, null, options);
    }

    /// <summary>
    /// SIU - Scheduling workflow
    /// Handles appointment booking, rescheduling, cancellation.
    /// </summary>
    private async Task<Result<string>> GenerateSchedulingAsync(string messageType, GenerationOptions options)
    {
        await Task.Yield();
        var encounterResult = _domainGenerationService.GenerateEncounter(options);
        if (!encounterResult.IsSuccess)
            return Result<string>.Failure(encounterResult.Error);

        var encounter = encounterResult.Value;
        var patient = encounter.Patient ?? _domainGenerationService.GeneratePatient(options).Value;

        if (patient == null)
            return Result<string>.Failure("Patient generation failed for SIU message");

        return await _hl7MessageFactory.GenerateMessageAsync(messageType, patient, encounter, null, null, null, options);
    }

    /// <summary>
    /// MDM - Medical Document Management workflow
    /// Handles clinical document creation, editing, addendums.
    /// </summary>
    private async Task<Result<string>> GenerateMedicalDocumentAsync(string messageType, GenerationOptions options)
    {
        await Task.Yield();
        var encounterResult = _domainGenerationService.GenerateEncounter(options);
        if (!encounterResult.IsSuccess)
            return Result<string>.Failure(encounterResult.Error);

        var encounter = encounterResult.Value;
        var patient = encounter.Patient ?? _domainGenerationService.GeneratePatient(options).Value;

        if (patient == null)
            return Result<string>.Failure("Patient generation failed for MDM message");

        return await _hl7MessageFactory.GenerateMessageAsync(messageType, patient, encounter, null, null, null, options);
    }

    /// <summary>
    /// QBP - Query workflow
    /// Handles data requests and patient searches.
    /// </summary>
    private async Task<Result<string>> GenerateQueryAsync(string messageType, GenerationOptions options)
    {
        await Task.Yield();
        var patientResult = _domainGenerationService.GeneratePatient(options);
        if (!patientResult.IsSuccess)
            return Result<string>.Failure(patientResult.Error);

        var patient = patientResult.Value;
        return await _hl7MessageFactory.GenerateMessageAsync(messageType, patient, null, null, null, null, options);
    }

    /// <summary>
    /// RSP - Query Response workflow  
    /// Handles responses to data queries.
    /// </summary>
    private async Task<Result<string>> GenerateQueryResponseAsync(string messageType, GenerationOptions options)
    {
        await Task.Yield();
        var patientResult = _domainGenerationService.GeneratePatient(options);
        if (!patientResult.IsSuccess)
            return Result<string>.Failure(patientResult.Error);

        var patient = patientResult.Value;
        return await _hl7MessageFactory.GenerateMessageAsync(messageType, patient, null, null, null, null, options);
    }

    /// <summary>
    /// ACK - Acknowledgment workflow
    /// System-to-system communication confirmations.
    /// </summary>
    private async Task<Result<string>> GenerateAcknowledgmentAsync(string messageType, GenerationOptions options)
    {
        await Task.Yield();
        var patientResult = _domainGenerationService.GeneratePatient(options);
        if (!patientResult.IsSuccess)
            return Result<string>.Failure(patientResult.Error);

        var patient = patientResult.Value;
        return await _hl7MessageFactory.GenerateMessageAsync(messageType, patient, null, null, null, null, options);
    }

    /// <summary>
    /// BAR / DFT - Financial workflow.
    /// Handles billing account management (BAR) and detailed financial transactions
    /// (DFT); both share the patient + encounter domain setup.
    /// </summary>
    private async Task<Result<string>> GenerateFinancialMessageAsync(string messageType, GenerationOptions options)
    {
        await Task.Yield();
        var encounterResult = _domainGenerationService.GenerateEncounter(options);
        if (!encounterResult.IsSuccess)
            return Result<string>.Failure(encounterResult.Error);

        var encounter = encounterResult.Value;
        var patient = encounter.Patient ?? _domainGenerationService.GeneratePatient(options).Value;

        if (patient == null)
            return Result<string>.Failure($"Patient generation failed for {messageType} message");

        return await _hl7MessageFactory.GenerateMessageAsync(messageType, patient, encounter, null, null, null, options);
    }

    /// <summary>
    /// VXU - Immunization workflow
    /// Handles unsolicited vaccination record updates.
    /// </summary>
    private async Task<Result<string>> GenerateImmunizationAsync(string messageType, GenerationOptions options)
    {
        await Task.Yield();
        var encounterResult = _domainGenerationService.GenerateEncounter(options);
        if (!encounterResult.IsSuccess)
            return Result<string>.Failure(encounterResult.Error);

        var encounter = encounterResult.Value;
        var patient = encounter.Patient ?? _domainGenerationService.GeneratePatient(options).Value;

        if (patient == null)
            return Result<string>.Failure("Patient generation failed for VXU message");

        return await _hl7MessageFactory.GenerateMessageAsync(messageType, patient, encounter, null, null, null, options);
    }

    // === Helper Methods ===

    /// <summary>
    /// Pins the scenario coordinator to the named condition carried on the options, so every
    /// segment contributor (DG1/OBX/RXE) draws from that one condition's panel. Called after
    /// <see cref="ClinicalScenarioCoordinator.ApplySeed"/> so a seeded run stays reproducible.
    /// No-op when no condition is named (random weighted scenario selection is unchanged).
    /// </summary>
    private void PinScenarioCondition(GenerationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ClinicalScenarioId))
            _scenarioCoordinator?.InitializeScenario(options.ClinicalScenarioId);
    }

    private static string ExtractBaseMessageType(string messageType)
    {
        if (string.IsNullOrWhiteSpace(messageType))
            return messageType;
            
        // Handle HL7 v2 format: ADT^A01 -> ADT
        var caretIndex = messageType.IndexOf('^');
        return caretIndex > 0 ? messageType.Substring(0, caretIndex) : messageType;
    }

}