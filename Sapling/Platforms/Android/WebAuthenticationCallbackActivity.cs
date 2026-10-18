using Android.App;
using Android.Content;
using Android.Content.PM;
using Sapling.Shared.Contracts;

namespace Sapling;

/// <summary>Receives the sapling://auth redirect that ends Google sign-in and hands it back to WebAuthenticator.</summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = ExternalAuth.AppScheme,
    DataHost = ExternalAuth.AppHost)]
public class WebAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity
{
}
