// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Default <see cref="IComponentImportanceClassifier"/>: the composite-component importance
/// heuristics. Derives each component's population tier from its name, position, and parent
/// field data type (XAD / XPN / CX / XTN / CE / CWE). A pure function of its inputs; carries
/// no state.
/// </summary>
public class ComponentImportanceClassifier : IComponentImportanceClassifier
{
    public ComponentImportance GetComponentImportance(DataTypeComponent component, SegmentField parentField)
    {
        var fieldName = component.Name?.ToLowerInvariant() ?? "";

        // XAD (Extended Address)
        if (parentField.DataType == "XAD")
        {
            // Critical (95%): Core address components
            if (fieldName.Contains("street") || fieldName.Contains("city") ||
                fieldName.Contains("state") || fieldName.Contains("zip") || fieldName.Contains("postal") ||
                fieldName.Contains("address type") || component.Position == 7)
                return ComponentImportance.Critical;

            // Important (50%): Suite/apt, country
            if (fieldName.Contains("other designation") || component.Position == 2 ||
                fieldName.Contains("country") || component.Position == 6)
                return ComponentImportance.Important;

            // Optional (20%): Census tract, county, other geographic
            return ComponentImportance.Optional;
        }

        // XPN (Extended Person Name)
        if (parentField.DataType == "XPN")
        {
            // Critical (95%): Core name components
            if (fieldName.Contains("family") || fieldName.Contains("given") || fieldName.Contains("first"))
                return ComponentImportance.Critical;

            // Important (50%): Middle name/initial, prefix, suffix
            if (fieldName.Contains("middle") || component.Position == 3 ||
                fieldName.Contains("prefix") || component.Position == 4 ||
                fieldName.Contains("suffix") || component.Position == 5)
                return ComponentImportance.Important;

            // Optional (20%): Degree, name type code, validity range
            return ComponentImportance.Optional;
        }

        // CX (Extended Composite ID)
        if (parentField.DataType == "CX")
        {
            // Critical (95%): ID value, check digit, check digit scheme
            if (component.Position == 1 || component.Position == 2 || component.Position == 3 ||
                (fieldName.Contains("id") && !fieldName.Contains("assigning")) ||
                fieldName.Contains("check digit"))
                return ComponentImportance.Critical;

            // Important (50%): Assigning authority, identifier type
            if (component.Position == 4 || component.Position == 5 ||
                fieldName.Contains("assigning authority") || fieldName.Contains("identifier type"))
                return ComponentImportance.Important;

            // Optional (20%): Assigning facility
            return ComponentImportance.Optional;
        }

        // XTN (Extended Telecommunication)
        if (parentField.DataType == "XTN")
        {
            // Critical (95%): Phone number, use code, equipment type
            if (component.Position == 1 || component.Position == 2 || component.Position == 3 ||
                (fieldName.Contains("telephone") && !fieldName.Contains("use") && !fieldName.Contains("equipment")))
                return ComponentImportance.Critical;

            // Important (50%): Email, area code
            if (fieldName.Contains("email") || component.Position == 4 ||
                fieldName.Contains("area") || component.Position == 6)
                return ComponentImportance.Important;

            // Optional (20%): Country code, extension, any text
            return ComponentImportance.Optional;
        }

        // CE/CWE (Coded Element): Identifier and Text are critical, alternates are important
        if (parentField.DataType == "CE" || parentField.DataType == "CWE")
        {
            // Critical (95%): Primary identifier and text
            if (component.Position <= 2 || fieldName.Contains("identifier") || fieldName.Contains("text"))
                return ComponentImportance.Critical;

            // Important (50%): Coding system, alternate codes
            if (component.Position == 3 || fieldName.Contains("coding system") ||
                fieldName.Contains("alternate"))
                return ComponentImportance.Important;

            // Optional (20%): Rarely used alternate system fields
            return ComponentImportance.Optional;
        }

        // Default: Optional (20% population)
        return ComponentImportance.Optional;
    }
}
