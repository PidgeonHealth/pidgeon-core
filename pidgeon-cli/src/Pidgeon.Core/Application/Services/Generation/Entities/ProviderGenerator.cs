// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.Generation.Entities;

/// <summary>
/// Per-entity generator for <see cref="Provider"/>. Each entity owns its own
/// generation logic.
/// </summary>
internal sealed class ProviderGenerator
{
    private static readonly string[] Specialties =
    {
        "Family Medicine", "Internal Medicine", "Cardiology", "Pediatrics", "Psychiatry"
    };

    private readonly IDemographicsDataService _demographics;
    private readonly LockedValueApplier _lockedValues;
    private readonly ILogger<ProviderGenerator> _logger;

    public ProviderGenerator(
        IDemographicsDataService demographics,
        LockedValueApplier lockedValues,
        ILogger<ProviderGenerator> logger)
    {
        _demographics = demographics ?? throw new ArgumentNullException(nameof(demographics));
        _lockedValues = lockedValues ?? throw new ArgumentNullException(nameof(lockedValues));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Provider Generate(EntityGenerationContext ctx)
    {
        var rng = ctx.Key.Stream();

        var nameTask = _demographics.GenerateRandomNameAsync(ctx.Key.Derive("name").AsRandom());
        var (firstName, lastName, _) = nameTask.IsCompletedSuccessfully
            ? nameTask.Result
            : nameTask.ConfigureAwait(false).GetAwaiter().GetResult();
        var npi = $"1{rng.Next(100000000, 999999999)}";

        var provider = new Provider
        {
            Id = npi,
            Name = PersonName.Create(lastName, firstName),
            NpiNumber = npi,
            Specialty = Specialties[rng.Next(Specialties.Length)]
        };

        var applyTask = _lockedValues.ApplyAsync(provider, ctx.Options, "Provider");
        provider = applyTask.IsCompletedSuccessfully
            ? applyTask.Result
            : applyTask.ConfigureAwait(false).GetAwaiter().GetResult();

        return provider;
    }

    public Task<Provider> GenerateAsync(EntityGenerationContext ctx) =>
        Task.FromResult(Generate(ctx));
}
