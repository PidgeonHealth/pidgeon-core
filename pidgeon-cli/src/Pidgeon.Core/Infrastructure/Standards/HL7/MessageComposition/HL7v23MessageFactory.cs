// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Threading;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;
using Microsoft.Extensions.Logging;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Universal HL7 v2.3 message factory that generates any message type using trigger event data.
/// No hardcoded message types - dynamically reads trigger event JSON and builds messages.
/// </summary>
public class HL7v23MessageFactory : IHL7MessageFactory
{
    private readonly ILogger<HL7v23MessageFactory> _logger;
    private readonly HL7MessageComposer _composer;

    public HL7v23MessageFactory(ILogger<HL7v23MessageFactory> logger, HL7MessageComposer composer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
    }

    /// <summary>
    /// Universal message generation method - takes any HL7 message type and generates it
    /// using the corresponding trigger event JSON definition from the installed data composition.
    /// </summary>
    /// <param name="messageType">Any HL7 message type (e.g., "ADT^A01", "ORM^O01", "ORU^R01")</param>
    /// <param name="patient">Patient data (required for all messages)</param>
    /// <param name="encounter">Encounter data (optional, for visit-related messages)</param>
    /// <param name="prescription">Prescription data (optional, for pharmacy messages)</param>
    /// <param name="observation">Observation data (optional, for lab messages)</param>
    /// <param name="order">Order data (optional, for order messages)</param>
    /// <param name="options">Generation options</param>
    /// <returns>Standards-compliant HL7 message generated from trigger event definition</returns>
    public async Task<Result<string>> GenerateMessageAsync(
        string messageType,
        Patient patient,
        Encounter? encounter = null,
        Prescription? prescription = null,
        ObservationResult? observation = null,
        Order? order = null,
        GenerationOptions? options = null)
    {
        try
        {
            _logger.LogDebug("Generating HL7 message {MessageType} using data-driven approach", messageType);

            return await _composer.ComposeMessageAsync(messageType, patient, encounter, prescription, observation, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating HL7 message {MessageType}", messageType);
            return Result<string>.Failure($"Failed to generate {messageType}: {ex.Message}");
        }
    }

}