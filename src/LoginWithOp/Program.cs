using LoginWithOp;
using OpenPayments.Sdk.Extensions;
using OpenPayments.Sdk.HttpSignatureUtils;

const string LoginCookie = "op_login";
const string SessionCookie = "op_session";
const string CallbackPath = "/callback";

LoginConfig? config;
try
{
    config = LoginConfig.Load(args, Directory.GetCurrentDirectory());
}
catch (ArgumentException error)
{
    Console.Error.WriteLine($"login-with-op: {error.Message}");
    return 1;
}
if (config is null)
{
    Console.WriteLine(LoginConfig.Usage);
    return 0;
}

// Flags are parsed above, so don't hand args to ASP.NET's own configuration.
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls($"http://localhost:{config.Port}");
builder.Services.UseOpenPayments(options =>
{
    options.UseAuthenticatedClient = true;
    options.KeyId = config.KeyId;
    options.PrivateKey = KeyUtils.LoadPem(config.PrivateKeyPem);
    options.ClientUrl = new Uri(config.WalletAddress);
});
builder.Services.AddSingleton<LoginStore>();
builder.Services.AddSingleton<LoginProvider>();

var app = builder.Build();
var callbackUrl = new Uri(config.PublicUrl ?? new Uri($"http://localhost:{config.Port}"), CallbackPath);

// Lax so the cookie survives the top-level redirect back from the wallet's authorization server.
static CookieOptions CookieOptionsFor(string path) => new() { Path = path, HttpOnly = true, SameSite = SameSiteMode.Lax };

app.MapGet("/", (HttpRequest request, LoginStore store) =>
    request.Cookies[SessionCookie] is { } sessionId && store.FindSession(sessionId) is { } session
        ? Pages.LoggedIn(session)
        : Pages.LoginForm());

app.MapPost("/login", async (HttpContext context, LoginProvider provider) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var rawWalletAddress = form["walletAddress"].ToString();
    var outcome = await provider.StartAsync(rawWalletAddress, callbackUrl, context.RequestAborted);
    switch (outcome)
    {
        case LoginStartOutcome.Redirect redirect:
            context.Response.Cookies.Append(LoginCookie, redirect.LoginId, CookieOptionsFor(CallbackPath));
            return Results.Redirect(redirect.RedirectUrl.ToString(), preserveMethod: false);
        case LoginStartOutcome.Failed failed:
            return Pages.LoginForm(rawWalletAddress, failed.Reason, 400);
        default:
            throw new InvalidOperationException($"unexpected outcome {outcome}");
    }
}).DisableAntiforgery();

app.MapGet(CallbackPath, async (HttpContext context, LoginProvider provider) =>
{
    var query = context.Request.Query;
    var outcome = await provider.FinishAsync(
        new LoginCallback(
            context.Request.Cookies[LoginCookie],
            query["interact_ref"].FirstOrDefault(),
            query["hash"].FirstOrDefault(),
            query["result"].FirstOrDefault()),
        context.RequestAborted);
    context.Response.Cookies.Delete(LoginCookie, CookieOptionsFor(CallbackPath));
    switch (outcome)
    {
        case LoginFinishOutcome.Verified verified:
            context.Response.Cookies.Append(SessionCookie, verified.Session.Id, CookieOptionsFor("/"));
            return Results.Redirect("/");
        case LoginFinishOutcome.Failed failed:
            return Pages.LoginFailed(failed.Reason);
        default:
            throw new InvalidOperationException($"unexpected outcome {outcome}");
    }
});

app.MapPost("/logout", (HttpContext context, LoginStore store) =>
{
    if (context.Request.Cookies[SessionCookie] is { } sessionId)
    {
        store.DeleteSession(sessionId);
    }
    context.Response.Cookies.Delete(SessionCookie, CookieOptionsFor("/"));
    return Results.Redirect("/");
}).DisableAntiforgery();

app.MapFallback(() => Pages.NotFound());

app.Logger.LogInformation("login-with-op: listening on http://localhost:{Port}", config.Port);
await app.RunAsync();
return 0;
