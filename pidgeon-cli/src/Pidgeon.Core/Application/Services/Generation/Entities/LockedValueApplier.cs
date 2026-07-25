// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Configuration;
using Pidgeon.Core.Common;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.Generation.Entities;

/// <summary>
/// Applies locked field values from a generation session to a freshly generated
/// clinical entity. Keeps per-entity generators focused on their own concerns
/// and contains the sync-over-async bridge in a single, well-documented location.
/// </summary>
internal sealed class LockedValueApplier
{
    private readonly ILockSessionService _lockSessionService;
    private readonly ILogger<LockedValueApplier> _logger;

    public LockedValueApplier(
        ILockSessionService lockSessionService,
        ILogger<LockedValueApplier> logger)
    {
        _lockSessionService = lockSessionService ?? throw new ArgumentNullException(nameof(lockSessionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<T> ApplyAsync<T>(T entity, GenerationOptions options, string messageType) where T : class
    {
        // Ad-hoc values are pinned per-request and take precedence over a named session:
        // the values the caller just supplied win over a saved profile. They apply directly
        // through the same semantic-path switch without touching the session store.
        if (options.AdhocLockedValues is { Count: > 0 } adhocValues)
        {
            var adhocEntity = entity;
            foreach (var adhoc in adhocValues)
            {
                // HL7-positional paths (PID.5.1, OBX.5, etc.) belong to the message composer's
                // pin-overlay pipeline, not the entity-level semantic switch. Skip them here so
                // we don't pretend to apply something we can't map to a clinical entity.
                if (Hl7Path.LooksPositional(adhoc.FieldPath)) continue;
                adhocEntity = ApplySemanticPathValue(adhocEntity, adhoc.FieldPath, adhoc.Value, messageType);
            }

            return adhocEntity;
        }

        if (string.IsNullOrEmpty(options.LockSessionName))
        {
            return entity;
        }

        try
        {
            var sessionResult = await _lockSessionService.GetSessionAsync(options.LockSessionName, CancellationToken.None);
            if (sessionResult.IsFailure)
            {
                _logger.LogWarning("Could not load lock session {SessionName}: {Error}",
                    options.LockSessionName, sessionResult.Error.Message);
                return entity;
            }

            var session = sessionResult.Value;
            if (!session.LockedValues.Any())
            {
                return entity;
            }

            _logger.LogDebug("Applying {Count} locked values from session {SessionName} to {EntityType}",
                session.LockedValues.Count, options.LockSessionName, typeof(T).Name);

            var modifiedEntity = entity;
            foreach (var lockedValue in session.LockedValues)
            {
                // HL7-positional paths handled by the composer's pin pipeline, not here.
                if (Hl7Path.LooksPositional(lockedValue.FieldPath)) continue;
                modifiedEntity = ApplySemanticPathValue(modifiedEntity, lockedValue.FieldPath, lockedValue.Value, messageType);
            }

            return modifiedEntity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply locked values from session {SessionName}", options.LockSessionName);
            return entity;
        }
    }

    private T ApplySemanticPathValue<T>(T entity, string semanticPath, string value, string messageType) where T : class
    {
        try
        {
            return entity switch
            {
                Patient patient => (T)(object)ApplyPatientSemanticPath(patient, semanticPath, value),
                Provider provider => (T)(object)ApplyProviderSemanticPath(provider, semanticPath, value),
                Encounter encounter => (T)(object)ApplyEncounterSemanticPath(encounter, semanticPath, value),
                _ => entity
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply semantic path {SemanticPath} = '{Value}' to {EntityType}",
                semanticPath, value, typeof(T).Name);
            return entity;
        }
    }

    private static Patient ApplyPatientSemanticPath(Patient patient, string semanticPath, string value)
    {
        return semanticPath.ToLowerInvariant() switch
        {
            "patient.mrn" => patient with
            {
                MedicalRecordNumber = value,
                Id = value
            },

            "patient.lastname" => patient with
            {
                Name = PersonName.Create(value, patient.Name?.Given)
            },

            "patient.firstname" => patient with
            {
                Name = PersonName.Create(patient.Name?.Family, value)
            },

            "patient.dateofbirth" when DateTime.TryParseExact(value, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var dob) =>
                patient with { BirthDate = dob },

            "patient.dateofbirth" when DateTime.TryParse(value, out var dobAlt) =>
                patient with { BirthDate = dobAlt },

            "patient.sex" => patient with
            {
                Gender = value.ToUpperInvariant() switch
                {
                    "M" => Gender.Male,
                    "F" => Gender.Female,
                    _ => Gender.Unknown
                }
            },

            "patient.phonenumber" => patient with { PhoneNumber = value },
            "patient.ssn" => patient with { SocialSecurityNumber = value },
            _ => patient
        };
    }

    private static Provider ApplyProviderSemanticPath(Provider provider, string semanticPath, string value)
    {
        return semanticPath.ToLowerInvariant() switch
        {
            "provider.id" => provider with
            {
                Id = value,
                NpiNumber = value
            },

            "provider.lastname" => provider with
            {
                Name = PersonName.Create(value, provider.Name?.Given)
            },

            "provider.firstname" => provider with
            {
                Name = PersonName.Create(provider.Name?.Family, value)
            },

            _ => provider
        };
    }

    private static Encounter ApplyEncounterSemanticPath(Encounter encounter, string semanticPath, string value)
    {
        return semanticPath.ToLowerInvariant() switch
        {
            "encounter.location" => encounter with { Location = value },
            "encounter.facility" => encounter with { Location = value },
            _ => encounter
        };
    }
}
