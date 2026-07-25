// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core.Application.Interfaces.Semantic;
using Pidgeon.Core.Application.Interfaces.Standards.HL7;
using Pidgeon.Core.Application.Services.Semantic;
using Pidgeon.Core.Infrastructure.Standards.HL7;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;
using Pidgeon.Core.Infrastructure.Standards.HL7.Reference;
using Pidgeon.Core.Services.FieldValueResolvers;

namespace Pidgeon.Core.Extensions;

/// <summary>
/// Internal registration helpers for <c>AddPidgeonHl7Generation()</c>: the HL7
/// message-composition graph and the field-value resolver chain. Lifetimes mirror the
/// private monorepo composition (<c>AddHL7DataProviders</c> / <c>AddFieldValueResolvers</c>)
/// verbatim — the Singleton/Scoped mix encodes per-message state boundaries and
/// per-process caches, and re-deciding it here would silently change determinism and
/// state semantics between compositions.
/// </summary>
internal static class CommunityHl7CompositionRegistrationExtensions
{
    /// <summary>
    /// Registers the HL7 composition graph over the package-neutral data seam: data
    /// providers, value contributors, per-segment composers, and the post-assembly
    /// passes. Exclusions from the monorepo graph are deliberate: the pharmacy order
    /// chain (<c>OrderMedicationSource</c> + <c>RxeValueContributor</c>) requires the
    /// private clinical data-source family, and RXE is outside the community
    /// capability contract (HL7 v2.3 ADT + ORU).
    /// </summary>
    internal static IServiceCollection AddCommunityHl7CompositionGraph(this IServiceCollection services)
    {
        // Data providers: Singletons that lazy-load immutable reference data through the
        // registered IDataResourceResolver into per-process caches.
        services.TryAddSingleton<IHL7TriggerEventProvider, HL7TriggerEventProvider>();
        services.TryAddSingleton<IHL7SegmentProvider, HL7SegmentProvider>();
        services.TryAddSingleton<IHL7DataTypeProvider, HL7DataTypeProvider>();
        services.TryAddSingleton<IHL7TableProvider, HL7TableProvider>();
        services.TryAddSingleton<IHL7DataProviderFactory, HL7DataProviderFactory>();

        // Fallback coded values (table/keyword/constraint sections). Absent data yields
        // empty sections and consumers fall through to their next strategy.
        services.TryAddSingleton<IHL7FallbackValueSource, HL7FallbackValueSource>();

        // Demographic reference lists for table-driven value selection. Absent data
        // yields empty lists and the consuming field falls through.
        services.TryAddSingleton<IDemographicTableProvider, EmbeddedDemographicTableProvider>();

        // Narrative-note corpus: an NTE leg with no corpus emits nothing, never boilerplate.
        services.TryAddSingleton<INarrativeNoteCorpus, NarrativeNoteCorpusLoader>();

        // Value contributors own segment content as semantics. Distinct concrete types,
        // so TryAddEnumerable is both safe and idempotent.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValueContributor, Dg1ValueContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValueContributor, ObxValueContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValueContributor, NteValueContributor>());
        services.TryAddSingleton<Hl7NarrativeNoteComposer>();
        services.TryAddSingleton<Hl7CorpusNoteComposer>();
        services.TryAddScoped<ValueContributorRegistry>();
        services.TryAddScoped<ICoherentValueSetSerializer, CoherentValueSetSerializer>();

        // Field pipeline collaborators.
        services.TryAddScoped<IHL7FieldPinner, HL7FieldPinner>();
        services.TryAddScoped<IComponentImportanceClassifier, ComponentImportanceClassifier>();
        services.TryAddScoped<IHL7TableValueSource, HL7TableValueSource>();
        services.TryAddScoped<IHL7VersionResolver, HL7VersionResolver>();
        services.TryAddScoped<ISegmentInclusionPolicy, SegmentInclusionPolicy>();
        services.TryAddScoped<ISegmentRepeatResolver, SegmentRepeatResolver>();

        // MSH header identity defaults + composition.
        services.TryAddSingleton<MshHeaderDefaults>();
        services.TryAddScoped<IMshSegmentComposer, MshSegmentComposer>();

        // The single CE/CWE coded-element renderer shared by the field pipeline and the
        // value-set serializer.
        services.TryAddSingleton<CodedElementRenderer>();

        // Field-value pipeline and per-segment composition.
        services.TryAddScoped<IHL7FieldComposer, HL7FieldComposer>();
        services.TryAddScoped<ISegmentComposer, SegmentComposer>();

        // Post-assembly passes over the assembled message string.
        services.TryAddSingleton<ITemporalCoherencePass, TemporalCoherencePass>();
        services.TryAddSingleton<IZSegmentComposer, ZSegmentComposer>();
        services.TryAddSingleton<IVendorDialectPass, VendorDialectPass>();

        // The data-driven composer itself.
        services.TryAddScoped<HL7MessageComposer>();

        return services;
    }

    /// <summary>
    /// Registers the priority-ordered field-value resolver chain. Resolvers whose
    /// constructors require the private clinical data-source family
    /// (Demographic/Medication/Diagnosis/LabTest/IdentifierCoherence/ClinicalCodedElement),
    /// the semantic dataset loaders (AllergyCodedElement), or the configuration
    /// field-path service (SemanticPath) are deliberately absent — the chain falls
    /// through and <see cref="SmartRandomResolver"/> remains the floor, so every field
    /// still resolves a value.
    /// </summary>
    internal static IServiceCollection AddCommunityFieldValueResolvers(this IServiceCollection services)
    {
        services.TryAddScoped<IFieldValueResolverService, FieldValueResolverService>();

        // Temporal coherence (timestamp relationships) — plain + composite-aware roles.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFieldValueResolver, TemporalCoherenceResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICompositeAwareResolver, TemporalCoherenceResolver>());

        // HL7-specific fields (MSH fields, message structure) + SFT software identity.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFieldValueResolver,
            Infrastructure.Standards.HL7.Resolvers.HL7SpecificFieldResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFieldValueResolver,
            Infrastructure.Standards.HL7.Resolvers.SoftwareSegmentResolver>());

        // Patient demographic coherence sourced from the Patient entity.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFieldValueResolver, PatientDemographicResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICompositeAwareResolver, PatientDemographicResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICompositeAwareResolver, CommunityPatientNameResolver>());

        // RXA administration quantities.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFieldValueResolver, VaccineAdministrationResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICompositeAwareResolver, VaccineAdministrationResolver>());

        // Specimen ↔ analyte coherence (OBR-15 / SPM-4).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFieldValueResolver, SpecimenCoherenceResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICompositeAwareResolver, SpecimenCoherenceResolver>());

        // HL7 table-driven coded values.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFieldValueResolver,
            Infrastructure.Standards.HL7.Resolvers.HL7TableFieldResolver>());

        // Composite-aware coded element + range coherence.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICompositeAwareResolver, CodedElementResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICompositeAwareResolver, RangeCoherenceResolver>());

        // Clinical scenario resolvers (diagnosis, medication, lab results).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFieldValueResolver, ClinicalDiagnosisResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFieldValueResolver, ClinicalMedicationResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFieldValueResolver, ClinicalLabResultResolver>());

        // Identifier and contact fields.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFieldValueResolver, IdentifierFieldResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFieldValueResolver, ContactFieldResolver>());

        // HL7 coded values (language codes, status codes, types).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFieldValueResolver, HL7CodedValueResolver>());

        // Smart random fallback — always provides a value; the chain's floor.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFieldValueResolver, SmartRandomResolver>());

        return services;
    }
}
