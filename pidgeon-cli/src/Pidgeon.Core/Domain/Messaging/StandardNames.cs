// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Messaging;

/// <summary>
/// The canonical healthcare standard-name vocabulary for the agent and judge
/// surfaces. One home for the machine tokens (the values a tool's
/// <c>"standard"</c> enum offers the model), the prose display names (how the
/// standards are spelled in tool descriptions and the system prompt), and the
/// composed surface strings built from them.
///
/// The tokens are lowercase to match <c>MessageTypeVocabulary.InferStandard</c>.
/// Surfacing a new standard on the agent/judge surface (e.g. X12 or C-CDA) is a
/// data change here — add a token, a display name, and extend the relevant
/// composed string — rather than new literals sprinkled across Core/Services.
///
/// Scope boundary: this owns the machine contract (the enum tokens a tool offers
/// the model) and the standard names used in prose. It deliberately does NOT
/// absorb deeply-embedded, format-specific input documentation inside a tool
/// schema (e.g. "For HL7, paste the pipe-delimited message; for FHIR, paste the
/// JSON") — that copy is irreducibly per-standard and is left inline as
/// documentation; centralizing it would not make adding a standard a data change.
/// </summary>
public static class StandardNames
{
    // ---- machine tokens (enum values; lowercase, match InferStandard) -------

    public const string Hl7 = "hl7";
    public const string Fhir = "fhir";
    public const string Ncpdp = "ncpdp";

    // ---- prose display names ------------------------------------------------

    public const string Hl7Name = "HL7";
    public const string Hl7Versioned = "HL7 v2";
    public const string FhirVersioned = "FHIR R4";
    public const string NcpdpName = "NCPDP";
    public const string NcpdpScript = "NCPDP SCRIPT";

    // ---- composed surface strings -------------------------------------------

    /// <summary>The standards the agent tools describe, e.g. "Validate an {…} message".</summary>
    public const string AgentSurfaceListOr = Hl7Versioned + ", " + FhirVersioned + ", or " + NcpdpScript;

    /// <summary>The standards named in the agent system prompt.</summary>
    public const string AgentSurfaceList = Hl7Versioned + ", " + FhirVersioned + ", " + NcpdpName;

    // ---- JSON enum arrays (injected into the tool schemas) ------------------

    /// <summary>The <c>"standard"</c> enum for tools that accept all three standards.</summary>
    public const string ValidateStandardsEnumJson = "[\"" + Hl7 + "\", \"" + Fhir + "\", \"" + Ncpdp + "\"]";

    /// <summary>The <c>"standard"</c> enum for tools that accept HL7 and FHIR only.</summary>
    public const string ExplainStandardsEnumJson = "[\"" + Hl7 + "\", \"" + Fhir + "\"]";
}
