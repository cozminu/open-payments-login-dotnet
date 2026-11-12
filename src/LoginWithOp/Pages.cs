using static System.Net.WebUtility;

namespace LoginWithOp;

/// <summary>Server-rendered HTML for the login form, the logged-in page and errors.</summary>
public static class Pages
{
    public static IResult LoginForm(string walletAddress = "", string? error = null, int status = 200) =>
        Page(
            status,
            "Log in with Open Payments",
            $"""
            {ErrorParagraph(error)}
            <form method="post" action="/login">
            <label for="walletAddress">Wallet address</label>
            <input id="walletAddress" name="walletAddress" required placeholder="https://ilp.interledger-test.dev/alice" value="{HtmlEncode(walletAddress)}">
            <button type="submit">Log in</button>
            </form>
            """);

    public static IResult LoggedIn(Session session) =>
        Page(
            200,
            "Logged in",
            $"""
            <p>You proved access to <code>{HtmlEncode(session.WalletAddress)}</code>.</p>
            <form method="post" action="/logout"><button type="submit">Log out</button></form>
            """);

    public static IResult LoginFailed(string reason) =>
        Page(400, "Login failed", $"""{ErrorParagraph(reason)}<p><a href="/">Try again</a></p>""");

    public static IResult NotFound() => Page(404, "Not found", """<p><a href="/">Home</a></p>""");

    private static string ErrorParagraph(string? error) =>
        error is null ? "" : $"""<p class="error">{HtmlEncode(error)}</p>""";

    private static IResult Page(int status, string title, string body) =>
        Results.Content(
            $$"""
            <!doctype html>
            <html lang="en">
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{HtmlEncode(title)}}</title>
            <style>body{font:16px/1.5 system-ui,sans-serif;max-width:36rem;margin:3rem auto;padding:0 1rem}input{width:100%;padding:.5rem;margin:.5rem 0;box-sizing:border-box}button{padding:.5rem 1rem}.error{color:#b00020;white-space:pre-wrap}</style>
            <h1>{{HtmlEncode(title)}}</h1>
            {{body}}
            </html>

            """,
            "text/html; charset=utf-8",
            statusCode: status);
}
