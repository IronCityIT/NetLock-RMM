using System;

namespace NetLock_RMM_Web_Console.Configuration
{
    // Iron Clad Support: product name and source-code offer for the web console.
    //
    // Both are overridable via appsettings (Webinterface:Title, Webinterface:SourceCodeUrl).
    // AGPL-3.0 section 13 requires offering the *modified* source to users interacting with the
    // console over a network, so the source link points at this fork, not at upstream.
    //
    // Kept free of web console dependencies so it can be compiled into the test project directly.
    public static class Branding
    {
        public const string Default_Product_Name = "Iron Clad Support";

        public const string Default_Source_Code_Url = "https://github.com/IronCityIT/NetLock-RMM";

        public static string Product_Name(string? configured) =>
            string.IsNullOrWhiteSpace(configured) ? Default_Product_Name : configured.Trim();

        // Only absolute http(s) URLs are accepted; anything else falls back to the fork.
        public static string Source_Code_Url(string? configured)
        {
            if (Uri.TryCreate(configured?.Trim(), UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                return uri.ToString().TrimEnd('/');

            return Default_Source_Code_Url;
        }

        public static string Issues_Url(string source_code_url) => source_code_url.TrimEnd('/') + "/issues";

        // Label shown in users' authenticator apps (issuer of the TOTP setup code)
        public static string Two_Factor_Issuer(string product_name) => product_name + " - Instance";
    }
}
