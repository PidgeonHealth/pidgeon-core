// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Interfaces.Data;

namespace Pidgeon.Core.Application.Services.Generation;

/// <summary>
/// Synchronous attestation gate for NCPDP SCRIPT generation. NCPDP is closed by default and
/// opens only once the operator has attested to the NCPDP membership / SCRIPT license. Two
/// signals count as attestation:
/// <list type="bullet">
///   <item>the member-supplied <c>ncpdp-script</c> data package is installed — which is itself
///   proof of acceptance, since the install pipeline refuses to install it without
///   <c>--accept-license</c>; and</item>
///   <item>the <c>PIDGEON_NCPDP_DEV_UNLOCK</c> dev/test bypass, which keeps the engine's NCPDP
///   suite running against the embedded XSDs without a package install.</item>
/// </list>
/// The decision is snapshotted at construction so the synchronous generation-plugin methods
/// never touch the filesystem per call.
/// </summary>
internal interface INcpdpAccessGate
{
    /// <summary>True when NCPDP SCRIPT generation is permitted (attested or dev-unlocked).</summary>
    bool IsAttested { get; }
}

/// <inheritdoc cref="INcpdpAccessGate"/>
internal sealed partial class NcpdpAccessGate : INcpdpAccessGate
{
    /// <summary>Registry name of the member-supplied SCRIPT XSD package.</summary>
    internal const string PackageName = "ncpdp-script";

    /// <summary>Env var that bypasses the gate for dev/test builds.</summary>
    internal const string DevUnlockEnvVar = "PIDGEON_NCPDP_DEV_UNLOCK";

    public bool IsAttested { get; }

    public NcpdpAccessGate(bool isAttested)
    {
        IsAttested = isAttested;
    }

    /// <summary>
    /// Resolves the gate from the dev-unlock env var and whether the <c>ncpdp-script</c> package
    /// is installed (its presence proves <c>--accept-license</c> was given at install time).
    /// Deliberately reads <see cref="IDataPackageCatalog.GetInstalledContentRoot"/> and never
    /// <c>FindInstalled</c>: the legacy adapter's <c>FindInstalled</c> also answers for
    /// registry-known-but-uninstalled packages, which would open the gate without any install —
    /// a straight license breach.
    /// </summary>
    public static NcpdpAccessGate Resolve(IDataPackageCatalog packages)
    {
        var devUnlock = Environment.GetEnvironmentVariable(DevUnlockEnvVar) == "1";
        return new NcpdpAccessGate(devUnlock || packages.GetInstalledContentRoot(PackageName) is not null);
    }
}
