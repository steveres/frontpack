namespace FrontPack.ServiceDefaults;

/// <summary>Token validation for Entra External ID ("Auth" section).</summary>
public sealed class AuthOptions
{
    /// <summary>e.g. https://contoso.ciamlogin.com/{tenantId}/v2.0</summary>
    public string Authority { get; set; } = "";
    /// <summary>The API app registration's client id (and/or its api:// URI).</summary>
    public string[] Audiences { get; set; } = [];
    /// <summary>Delegated scope the token must carry in "scp".</summary>
    public string RequiredScope { get; set; } = "FrontPack.Access";
    /// <summary>
    /// Entra dates its tokens' "iat" (issued at) about 5 minutes before they're really issued, to allow
    /// for clock differences. The logout cut-off allows for it, or a sign-in in the first minutes after
    /// a logout would get a token that looks older than the logout, and be turned away.
    /// </summary>
    public TimeSpan IssuedAtBackdating { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>"Cors" section.</summary>
public sealed class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>"Limits" section: the part every service shares (services add their own limits to the section).</summary>
public sealed class RateLimitOptions
{
    /// <summary>Requests per user per minute.</summary>
    public int RequestsPerMinute { get; set; } = 600;
}
