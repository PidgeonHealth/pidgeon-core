// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Generation.Types;

/// <summary>
/// AI providers for enhanced healthcare data generation (subscription tiers only).
/// Each provider offers different capabilities, costs, and compliance features.
/// </summary>
public enum AIProvider
{
    /// <summary>
    /// No AI enhancement - algorithmic generation only.
    /// Available in all tiers including free tier.
    /// </summary>
    None,

    /// <summary>
    /// OpenAI GPT models for contextual healthcare data generation.
    /// Excellent for general healthcare scenarios and clinical correlations.
    /// </summary>
    OpenAI,

    /// <summary>
    /// Anthropic Claude models for safety-focused healthcare generation.
    /// Preferred for sensitive healthcare contexts and ethical considerations.
    /// </summary>
    Anthropic,

    /// <summary>
    /// Azure OpenAI for enterprise compliance and data residency.
    /// Required for organizations with strict data governance requirements.
    /// </summary>
    AzureOpenAI,

    /// <summary>
    /// Hugging Face models for cost-effective specialized generation.
    /// Open-source models fine-tuned for specific healthcare domains.
    /// </summary>
    HuggingFace,

    /// <summary>
    /// Google PaLM/Gemini for healthcare-specific fine-tuned models.
    /// Specialized for medical terminology and clinical reasoning.
    /// </summary>
    Google,

    /// <summary>
    /// Local/on-premise AI models for maximum data privacy.
    /// Enterprise-only option for air-gapped environments.
    /// </summary>
    Local
}

/// <summary>
/// AI generation modes for different levels of enhancement and complexity.
/// </summary>
public enum AIGenerationMode
{
    /// <summary>
    /// Enhanced generation with improved correlations and realistic patterns.
    /// Basic AI improvement over algorithmic generation.
    /// </summary>
    Enhanced,

    /// <summary>
    /// Narrative generation including clinical notes and documentation.
    /// Generates realistic clinical narratives and provider notes.
    /// </summary>
    Narrative,

    /// <summary>
    /// Specialty-focused generation for specific medical domains.
    /// Oncology, cardiology, pediatrics, etc. with domain expertise.
    /// </summary>
    Specialty,

    /// <summary>
    /// Complex correlation generation with drug interactions and comorbidities.
    /// Advanced medical reasoning for realistic patient scenarios.
    /// </summary>
    Complex,

    /// <summary>
    /// Workflow-aware generation for specific healthcare processes.
    /// Generates data appropriate for admission, discharge, treatment workflows.
    /// </summary>
    Workflow
}

/// <summary>
/// Extensions for working with AI providers and modes.
/// </summary>
public static class AIProviderExtensions
{
    /// <summary>
    /// Gets the display name for an AI provider.
    /// </summary>
    public static string GetDisplayName(this AIProvider provider) => provider switch
    {
        AIProvider.None => "Algorithmic Only",
        AIProvider.OpenAI => "OpenAI (GPT-3.5/4)",
        AIProvider.Anthropic => "Anthropic Claude",
        AIProvider.AzureOpenAI => "Azure OpenAI",
        AIProvider.HuggingFace => "Hugging Face",
        AIProvider.Google => "Google AI",
        AIProvider.Local => "Local AI Models",
        _ => provider.ToString()
    };

    /// <summary>
    /// Determines if the AI provider requires an API key (BYOK model).
    /// </summary>
    public static bool RequiresApiKey(this AIProvider provider) => provider switch
    {
        AIProvider.None or AIProvider.Local => false,
        _ => true // Most providers require API keys for BYOK
    };

    /// <summary>
    /// Gets the estimated cost tier for the AI provider.
    /// </summary>
    public static string GetCostTier(this AIProvider provider) => provider switch
    {
        AIProvider.None => "Free",
        AIProvider.HuggingFace => "Low Cost",
        AIProvider.OpenAI or AIProvider.Google => "Medium Cost",
        AIProvider.Anthropic or AIProvider.AzureOpenAI => "Higher Cost",
        AIProvider.Local => "Infrastructure Cost",
        _ => "Variable"
    };

    /// <summary>
    /// Determines if the provider is suitable for healthcare compliance.
    /// </summary>
    public static bool IsHealthcareCompliant(this AIProvider provider) => provider switch
    {
        AIProvider.AzureOpenAI or AIProvider.Local => true, // Enterprise compliance
        AIProvider.Anthropic => true, // Strong privacy focus
        _ => false // Requires careful evaluation for healthcare use
    };
}

/// <summary>
/// Extensions for AI generation modes.
/// </summary>
public static class AIGenerationModeExtensions
{
    /// <summary>
    /// Gets the complexity level of the generation mode.
    /// </summary>
    public static int GetComplexityLevel(this AIGenerationMode mode) => mode switch
    {
        AIGenerationMode.Enhanced => 1,
        AIGenerationMode.Narrative => 2,
        AIGenerationMode.Specialty => 3,
        AIGenerationMode.Complex => 4,
        AIGenerationMode.Workflow => 5,
        _ => 0
    };

    /// <summary>
    /// Gets the estimated token usage multiplier for the mode.
    /// </summary>
    public static double GetTokenMultiplier(this AIGenerationMode mode) => mode switch
    {
        AIGenerationMode.Enhanced => 1.5,
        AIGenerationMode.Narrative => 2.0,
        AIGenerationMode.Specialty => 2.5,
        AIGenerationMode.Complex => 3.0,
        AIGenerationMode.Workflow => 3.5,
        _ => 1.0
    };
}