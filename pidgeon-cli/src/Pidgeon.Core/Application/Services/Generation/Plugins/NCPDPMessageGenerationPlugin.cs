// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Generation;
using Pidgeon.Core.Infrastructure.Standards.NCPDP;

namespace Pidgeon.Core.Application.Services.Generation.Plugins;

/// <summary>
/// NCPDP-specific message generation plugin using hybrid approach.
/// Handles NCPDP SCRIPT transaction types for pharmacy workflows.
///
/// Generation is closed by default behind <see cref="INcpdpAccessGate"/>: until the operator
/// has attested to the NCPDP membership / SCRIPT license (or set the dev-unlock env var), the
/// plugin reports no supported types and handles nothing, so the CLI, Bridge, and capability
/// matrix all surface NCPDP as "license-required" automatically.
/// </summary>
internal partial class NCPDPMessageGenerationPlugin : IMessageGenerationPlugin
{
    private readonly INCPDPDataGenerator _dataGenerator;
    private readonly INCPDPXmlSerializer _serializer;
    private readonly INcpdpAccessGate _gate;
    private readonly ILogger<NCPDPMessageGenerationPlugin> _logger;

    public string StandardName => "ncpdp";

    public NCPDPMessageGenerationPlugin(
        INCPDPDataGenerator dataGenerator,
        INCPDPXmlSerializer serializer,
        INcpdpAccessGate gate,
        ILogger<NCPDPMessageGenerationPlugin> logger)
    {
        _dataGenerator = dataGenerator;
        _serializer = serializer;
        _gate = gate;
        _logger = logger;
    }

    /// <summary>
    /// NCPDP SCRIPT transaction types with rich healthcare context.
    /// Organized by pharmacy workflow and transaction purpose.
    /// </summary>
    private static readonly HashSet<string> _transactionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // === Core Prescription Workflow (Primary NCPDP Transactions) ===
        "NEWRX",         // New Prescription - Initial prescription order from prescriber to pharmacy
        "RXCHANGEREQUEST", // Prescription Change Request - Pharmacy requests changes to prescription
        "RXCHANGERESPONSE", // Prescription Change Response - Prescriber response to change request
        "REFILLREQUEST", // Refill Request - Pharmacy requests refill authorization
        "REFILLRESPONSE", // Refill Response - Prescriber approves/denies refill request
        "RXFILL",        // Prescription Fill - Pharmacy reports dispensing to prescriber
        "CANCELRX",      // Cancel Prescription - Cancel/void existing prescription order
        "CANCELRXRESPONSE", // Cancel Response - Acknowledgment of prescription cancellation

        // === Enhanced Prescription Management ===
        "RXHISTORYREQUEST",  // Prescription History Request - Request patient's medication history
        "RXHISTORYRESPONSE", // Prescription History Response - Patient medication history data
        "VERIFY",           // Prescription Verification - Verify prescription authenticity
        "STATUS",           // Status Update - General prescription status notification
        "ERROR",            // Error Notification - Report transaction processing errors
        "RESUPPLYREQUEST",  // Resupply Request - Request for medication resupply
        "RESUPPLYRESPONSE", // Resupply Response - Response to resupply request

        // === Specialty Pharmacy Transactions ===
        "PREDETERMINATION", // Prior Authorization - Insurance coverage determination
        "PRIORAUTH",       // Prior Authorization Request - Formal prior auth request
        "PRIORAUTHRESPONSE", // Prior Auth Response - Insurance decision on prior auth
        "BENEFITSCOORDINATION", // Benefits Coordination - Coordinate multiple insurance coverage
        "ELIGIBILITY",     // Insurance Eligibility - Verify patient insurance coverage
        "FORMULARY",       // Formulary Information - Drug formulary/coverage details

        // === Clinical Decision Support ===
        "DUR",            // Drug Utilization Review - Clinical appropriateness check
        "DRUGALERT",      // Drug Alert - Safety alerts and warnings
        "INTERACTION",    // Drug Interaction - Drug-drug interaction alerts
        "ALLERGY",        // Allergy Alert - Patient allergy warnings
        "CLINICALINFO",   // Clinical Information - Additional clinical context

        // === Administrative Transactions ===
        "DELIVERYRECEIPT", // Delivery Receipt - Confirm message delivery
        "ACKNOWLEDGMENT",  // General Acknowledgment - Confirm message processing
        "STRUCTURED",      // Structured Message - Complex multi-part message
        "FREEFORM",       // Free Form Message - Unstructured communication
        "RECERTIFICATION", // Recertification - Provider recertification request

        // === Reporting and Analytics ===
        "REPORTREQUEST",  // Report Request - Request for prescription reports
        "REPORTRESPONSE", // Report Response - Prescription reporting data
        "QUALITY",        // Quality Metrics - Quality measure reporting
        "AUDIT",          // Audit Information - Transaction audit trail
        "STATISTICS"      // Statistics - Usage and performance statistics
    };

    public bool CanHandleMessageType(string messageType)
    {
        return _gate.IsAttested &&
               !string.IsNullOrWhiteSpace(messageType) &&
               _transactionTypes.Contains(messageType);
    }

    public async Task<Result<IReadOnlyList<string>>> GenerateMessagesAsync(string messageType, int count, GenerationOptions? options = null)
    {
        // Defense-in-depth: the service path gates via CanHandleMessageType, but a direct caller
        // must not be able to generate NCPDP content while the license gate is closed.
        if (!_gate.IsAttested)
            return Result<IReadOnlyList<string>>.Failure(GetUnsupportedMessageTypeError(messageType));

        try
        {
            var messages = new List<string>();
            var generationOptions = options ?? new GenerationOptions();

            for (int i = 0; i < count; i++)
            {
                // The index is the message-instance coordinate (ADR-0005 §3): CreateKey folds it
                // into the entropy root so each transaction draws its own patient and control
                // number — without it a Count=N batch shares one coordinate and duplicates both.
                var messageOptions = generationOptions with { MessageIndex = i };

                var result = await GenerateSingleTransactionAsync(messageType, messageOptions);

                if (!result.IsSuccess)
                    return Result<IReadOnlyList<string>>.Failure(result.Error);

                messages.Add(result.Value);
            }

            return Result<IReadOnlyList<string>>.Success(messages);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<string>>.Failure($"NCPDP transaction generation failed: {ex.Message}");
        }
    }

    public IReadOnlyList<string> GetSupportedMessageTypes()
    {
        return _gate.IsAttested
            ? _transactionTypes.OrderBy(x => x).ToList()
            : Array.Empty<string>();
    }

    public string GetUnsupportedMessageTypeError(string messageType)
    {
        // Closed gate takes precedence: the type is fine, the license attestation is missing.
        if (!_gate.IsAttested)
        {
            return "NCPDP SCRIPT generation requires an NCPDP membership / SCRIPT license. " +
                   "After confirming your NCPDP membership, run " +
                   "`pidgeon data install ncpdp-script --accept-license` to enable NCPDP generation.";
        }

        var suggestions = new List<string>();

        // Common user mistakes with NCPDP-specific guidance
        var commonSuggestions = messageType.ToLowerInvariant() switch
        {
            "adt^a01" or "adt" => "NCPDP doesn't use HL7 v2 format. Use 'NEWRX' for new prescriptions",
            "rde^o11" or "rde" => "NCPDP doesn't use HL7 format. Use 'NEWRX' for prescription orders",
            "patient" => "NCPDP focuses on prescriptions. Use 'NEWRX' for new prescriptions or 'RXHISTORYREQUEST' for patient history",
            "prescription" => "NCPDP uses 'NEWRX' (new prescription), 'REFILLREQUEST' (refill), or 'RXFILL' (dispensing)",
            "medicationrequest" => "NCPDP doesn't use FHIR format. Use 'NEWRX' for prescription requests",
            "order" => "NCPDP uses 'NEWRX' for new prescription orders",
            "refill" => "NCPDP uses 'REFILLREQUEST' (request) or 'REFILLRESPONSE' (approval) for refills",
            "cancel" => "NCPDP uses 'CANCELRX' to cancel prescriptions",
            "fill" => "NCPDP uses 'RXFILL' to report prescription dispensing",
            "status" => "NCPDP has 'STATUS' transaction for prescription status updates",
            _ => null
        };

        if (commonSuggestions != null)
        {
            suggestions.Add(commonSuggestions);
        }

        // Find similar transaction names
        var similarTransactions = _transactionTypes
            .Where(t => t.StartsWith(messageType, StringComparison.OrdinalIgnoreCase) ||
                       messageType.StartsWith(t, StringComparison.OrdinalIgnoreCase) ||
                       t.Contains(messageType, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        if (similarTransactions.Any())
        {
            suggestions.Add($"Did you mean: {string.Join(", ", similarTransactions)}?");
        }

        var errorMessage = $"NCPDP standard doesn't support transaction type: {messageType}";
        if (suggestions.Any())
        {
            errorMessage += "\n\nSuggestions:\n" + string.Join("\n", suggestions.Select(s => $"  • {s}"));
        }
        else
        {
            var commonTransactions = new[] { "NEWRX", "RXFILL", "CANCELRX", "REFILLREQUEST", "RXHISTORYREQUEST" };
            errorMessage += $"\n\nCommon NCPDP transactions:\n  • {string.Join("\n  • ", commonTransactions)}";
        }

        return errorMessage;
    }

    /// <summary>
    /// Central routing logic - delegates to workflow-specific generation methods.
    /// </summary>
    private async Task<Result<string>> GenerateSingleTransactionAsync(string transactionType, GenerationOptions options)
    {
        return transactionType.ToUpperInvariant() switch
        {
            // Core Prescription Workflow — real XML generation
            "NEWRX" => await GenerateNewPrescriptionAsync(options),
            "RXFILL" => await GenerateRxFillAsync(options),
            "CANCELRX" => await GenerateCancelRxAsync(options),

            // Remaining transaction types — descriptive text stubs
            "RXCHANGEREQUEST" => await GenerateChangeRequestAsync(options),
            "RXCHANGERESPONSE" => await GenerateChangeResponseAsync(options),
            "REFILLREQUEST" => await GenerateRefillRequestAsync(options),
            "REFILLRESPONSE" => await GenerateRefillResponseAsync(options),
            "CANCELRXRESPONSE" => await GenerateCancelResponseAsync(options),

            // Enhanced Prescription Management
            "RXHISTORYREQUEST" => await GenerateHistoryRequestAsync(options),
            "RXHISTORYRESPONSE" => await GenerateHistoryResponseAsync(options),
            "VERIFY" => await GenerateVerificationAsync(options),
            "STATUS" => await GenerateStatusUpdateAsync(options),
            "ERROR" => await GenerateErrorNotificationAsync(options),

            // Specialty Pharmacy
            "PREDETERMINATION" => await GeneratePredeterminationAsync(options),
            "PRIORAUTH" => await GeneratePriorAuthAsync(options),
            "PRIORAUTHRESPONSE" => await GeneratePriorAuthResponseAsync(options),
            "ELIGIBILITY" => await GenerateEligibilityAsync(options),
            "FORMULARY" => await GenerateFormularyAsync(options),

            // Clinical Decision Support
            "DUR" => await GenerateDurReviewAsync(options),
            "DRUGALERT" => await GenerateDrugAlertAsync(options),
            "INTERACTION" => await GenerateInteractionAlertAsync(options),
            "ALLERGY" => await GenerateAllergyAlertAsync(options),
            "CLINICALINFO" => await GenerateClinicalInfoAsync(options),

            // Administrative
            "ACKNOWLEDGMENT" => await GenerateAcknowledgmentAsync(options),
            "STRUCTURED" => await GenerateStructuredMessageAsync(options),
            "FREEFORM" => await GenerateFreeFormMessageAsync(options),

            _ => Result<string>.Failure(GetUnsupportedMessageTypeError(transactionType))
        };
    }

    // === Core Prescription Workflow — Real XML Generation ===

    private async Task<Result<string>> GenerateNewPrescriptionAsync(GenerationOptions options)
    {
        try
        {
            var message = await _dataGenerator.GenerateNewRxAsync(options);
            var xml = _serializer.Serialize(message);
            return Result<string>.Success(xml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate NCPDP NewRx");
            return Result<string>.Failure($"NCPDP NewRx generation failed: {ex.Message}");
        }
    }

    private async Task<Result<string>> GenerateRxFillAsync(GenerationOptions options)
    {
        try
        {
            var message = await _dataGenerator.GenerateRxFillAsync(options);
            var xml = _serializer.Serialize(message);
            return Result<string>.Success(xml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate NCPDP RxFill");
            return Result<string>.Failure($"NCPDP RxFill generation failed: {ex.Message}");
        }
    }

    private async Task<Result<string>> GenerateCancelRxAsync(GenerationOptions options)
    {
        try
        {
            var message = await _dataGenerator.GenerateCancelRxAsync(options);
            var xml = _serializer.Serialize(message);
            return Result<string>.Success(xml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate NCPDP CancelRx");
            return Result<string>.Failure($"NCPDP CancelRx generation failed: {ex.Message}");
        }
    }
}
