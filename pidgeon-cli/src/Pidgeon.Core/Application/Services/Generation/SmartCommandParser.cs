// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core;

namespace Pidgeon.Core.Application.Services.Generation;

/// <summary>
/// Smart command line parser that supports both inference and explicit patterns.
/// Handles: pidgeon generate ADT^A01 (inference) and pidgeon generate hl7 ADT^A01 (explicit).
/// </summary>
public class SmartCommandParser
{
    /// <summary>
    /// Parses generate command arguments with smart inference support.
    /// </summary>
    /// <param name="args">Command arguments starting after 'generate'</param>
    /// <returns>Parsed generation request</returns>
    public static Result<GenerationRequest> Parse(string[] args)
    {
        if (args == null || args.Length == 0)
            return Result<GenerationRequest>.Failure("Message type is required");

        var firstArg = args[0];
        
        // Check if first argument is an explicit standard
        var knownStandards = new[] { "hl7", "fhir", "ncpdp" };
        if (knownStandards.Contains(firstArg.ToLowerInvariant()))
        {
            // Explicit pattern: pidgeon generate hl7 ADT^A01
            if (args.Length < 2)
                return Result<GenerationRequest>.Failure($"Message type is required after standard '{firstArg}'");
                
            return Result<GenerationRequest>.Success(new GenerationRequest
            {
                Standard = firstArg.ToLowerInvariant(),
                MessageType = args[1],
                ExplicitStandardProvided = true,
                RemainingArgs = args.Skip(2).ToArray()
            });
        }
        else
        {
            // Inference pattern: pidgeon generate ADT^A01
            var inferredStandard = Pidgeon.Core.Domain.Messaging.MessageTypeVocabulary.InferStandard(firstArg);
            if (inferredStandard == null)
            {
                var smartSuggestion = MessageTypeRegistry.GetSmartErrorSuggestion(firstArg);
                return Result<GenerationRequest>.Failure(smartSuggestion);
            }
            
            return Result<GenerationRequest>.Success(new GenerationRequest
            {
                Standard = inferredStandard,
                MessageType = MessageTypeRegistry.NormalizeForGeneration(firstArg),
                ExplicitStandardProvided = false,
                RemainingArgs = args.Skip(1).ToArray()
            });
        }
    }
}

/// <summary>
/// Represents a parsed generation request.
/// </summary>
public class GenerationRequest
{
    /// <summary>
    /// The healthcare standard (hl7, fhir, ncpdp).
    /// </summary>
    public required string Standard { get; init; }
    
    /// <summary>
    /// The message type (ADT^A01, Patient, NewRx, etc.).
    /// </summary>
    public required string MessageType { get; init; }
    
    /// <summary>
    /// Whether the user explicitly provided the standard.
    /// </summary>
    public bool ExplicitStandardProvided { get; init; }
    
    /// <summary>
    /// Remaining command line arguments for further parsing.
    /// </summary>
    public string[] RemainingArgs { get; init; } = Array.Empty<string>();
}