// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Data;

/// <summary>
/// Configuration for the AMA CPT API (WSO2 OAuth2).
/// </summary>
public class AmaApiOptions
{
    public string ConsumerKey { get; set; } = string.Empty;
    public string ConsumerSecret { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://sandbox-api.ama-assn.org/cpt-zip/1.0.0";
    public string TokenUrl { get; set; } = "https://sandbox-api.ama-assn.org/oauth2/token";
}
