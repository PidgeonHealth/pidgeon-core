// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Domain.Messaging.NCPDP;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.NCPDP;

/// <summary>
/// Generates NCPDP SCRIPT domain messages from synthetic clinical data.
/// Supports NewRx, RxFill, and CancelRx transaction types.
/// </summary>
public interface INCPDPDataGenerator
{
    /// <summary>
    /// Generates a synthetic NewRx (new prescription) message.
    /// </summary>
    /// <param name="options">Generation configuration including seeds and clinical context</param>
    /// <returns>A populated NCPDP message with NewRx body</returns>
    Task<NCPDPMessage> GenerateNewRxAsync(GenerationOptions? options = null);

    /// <summary>
    /// Maps a caller-supplied clinical <see cref="Prescription"/> into a NewRx message, instead of
    /// self-generating one from the seed. Lets a caller serialize a specific clinical reality to NCPDP
    /// (the cross-standard equivalence oracle feeds one shared domain instance to every standard;
    /// Migrate's cross-platform conversion has the same need).
    /// </summary>
    /// <param name="prescription">The prescription (patient + medication + prescriber + dosage) to render</param>
    /// <param name="options">Generation configuration; the seed still drives the non-clinical envelope (pharmacy, header)</param>
    /// <returns>A populated NCPDP message with NewRx body carrying the supplied prescription</returns>
    Task<NCPDPMessage> GenerateNewRxAsync(Prescription prescription, GenerationOptions? options = null);

    /// <summary>
    /// Generates a synthetic RxFill (prescription fill/dispensing) message.
    /// </summary>
    /// <param name="options">Generation configuration including seeds and clinical context</param>
    /// <returns>A populated NCPDP message with RxFill body</returns>
    Task<NCPDPMessage> GenerateRxFillAsync(GenerationOptions? options = null);

    /// <summary>
    /// Generates a synthetic CancelRx (prescription cancellation) message.
    /// </summary>
    /// <param name="options">Generation configuration including seeds and clinical context</param>
    /// <returns>A populated NCPDP message with CancelRx body</returns>
    Task<NCPDPMessage> GenerateCancelRxAsync(GenerationOptions? options = null);
}
