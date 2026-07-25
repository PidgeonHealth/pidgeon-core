// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Post-assembly temporal coherence pass for generator quality.
/// Runs once over a fully assembled HL7 message and guarantees that clinical-event
/// timestamps do not post-date MSH-7 for result/observation/event messages: a message
/// cannot be created before the events it reports. Preserves the relative ordering the
/// field resolver already established (specimen ≤ observation ≤ report; admit ≤ discharge)
/// by applying a single uniform backward shift, so it only ever moves events earlier,
/// never reorders them.
/// </summary>
public interface ITemporalCoherencePass
{
    /// <summary>
    /// Returns the message with clinical-event timestamps clamped to ≤ MSH-7 where the
    /// message intent requires it (result/observation/event messages). Returns the input
    /// unchanged when there is nothing to clamp (no MSH-7, no out-of-order event, or an
    /// order message whose requested times may legitimately be future).
    /// </summary>
    string Apply(string assembledMessage);
}
