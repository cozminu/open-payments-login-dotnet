using System.Text.RegularExpressions;
using OpenPayments.Sdk.Clients;
using OpenPayments.Sdk.Generated.Auth;
using OpenPayments.Sdk.Generated.Wallet;

namespace LoginWithOp;

/// <summary>Result of starting a login; on success, send the browser to <c>RedirectUrl</c>.</summary>
public abstract record LoginStartOutcome
{
    public sealed record Redirect(string LoginId, Uri RedirectUrl) : LoginStartOutcome;

    public sealed record Failed(string Reason) : LoginStartOutcome;
}

/// <summary>Result of finishing a login; <c>Verified</c> means the wallet's authorization server confirmed the user.</summary>
public abstract record LoginFinishOutcome
{
    public sealed record Verified(Session Session) : LoginFinishOutcome;

    public sealed record Failed(string Reason) : LoginFinishOutcome;
}

/// <summary>Raw query and cookie values seen on the finish callback, before any validation.</summary>
public sealed record LoginCallback(string? LoginId, string? InteractRef, string? Hash, string? Result);

/// <summary>Proves a user controls a wallet address with an interactive Open Payments subject grant.</summary>
public sealed partial class LoginProvider(IAuthenticatedClient client, LoginStore store, ILogger<LoginProvider> logger)
{
    /// <summary><c>$wallet.example/alice</c> style payment pointers; the docs and test wallet still show them.</summary>
    [GeneratedRegex(@"^\$(?<rest>[^\s/]+(?:/\S*)?)$")]
    private static partial Regex PaymentPointerPattern();

    /// <summary>Asks the wallet's authorization server to verify the user; <paramref name="callbackUrl"/> gets the finish redirect. Never throws.</summary>
    public async Task<LoginStartOutcome> StartAsync(string rawWalletAddress, Uri callbackUrl, CancellationToken cancellationToken)
    {
        var walletAddressUrl = ToWalletAddressUrl(rawWalletAddress);
        if (walletAddressUrl is null)
        {
            return new LoginStartOutcome.Failed($"wallet address \"{rawWalletAddress}\" is not a url or payment pointer");
        }

        WalletAddress walletAddress;
        try
        {
            walletAddress = await client.GetWalletAddressAsync(walletAddressUrl, cancellationToken);
        }
        catch (Exception error)
        {
            return new LoginStartOutcome.Failed(Describe(error, $"wallet address \"{walletAddressUrl}\" could not be resolved"));
        }

        // OriginalString keeps the exact value the wallet address returned; Uri.ToString() can add a
        // trailing slash, which would break both the subject match and the interaction hash.
        var walletAddressId = walletAddress.Id.OriginalString;
        var authServerUrl = walletAddress.AuthServer.OriginalString;
        var clientNonce = LoginStore.CreateId();

        AuthResponse grant;
        try
        {
            grant = await client.RequestGrantAsync(
                new RequestArgs { Url = walletAddress.AuthServer },
                new GrantCreateBodyWithInteract
                {
                    Subject = new Subject { Sub_ids = [new Sub_ids { Id = walletAddressId, Format = Sub_idsFormat.Uri }] },
                    Interact = new InteractRequest
                    {
                        Start = [Start.Redirect],
                        Finish = new Finish { Method = FinishMethod.Redirect, Uri = callbackUrl, Nonce = clientNonce },
                    },
                },
                cancellationToken);
        }
        catch (Exception error)
        {
            return new LoginStartOutcome.Failed(Describe(error, $"grant request to \"{authServerUrl}\" failed"));
        }

        if (grant.Interact is null || grant.Continue is null)
        {
            return new LoginStartOutcome.Failed($"authorization server \"{authServerUrl}\" did not ask for interaction");
        }

        var pendingLogin = store.SavePendingLogin(new PendingLogin(
            Id: "",
            WalletAddress: walletAddressId,
            AuthServerUrl: authServerUrl,
            ClientNonce: clientNonce,
            InteractNonce: grant.Interact.Finish,
            ContinueUrl: grant.Continue.Uri,
            ContinueAccessToken: grant.Continue.AccessToken.Value,
            ExpiresAt: default));
        return new LoginStartOutcome.Redirect(pendingLogin.Id, grant.Interact.Redirect);
    }

    /// <summary>Checks the finish redirect, continues the grant and opens a session. Never throws.</summary>
    public async Task<LoginFinishOutcome> FinishAsync(LoginCallback callback, CancellationToken cancellationToken)
    {
        if (callback.LoginId is null)
        {
            return new LoginFinishOutcome.Failed("no login in progress for this browser");
        }
        var pendingLogin = store.TakePendingLogin(callback.LoginId);
        if (pendingLogin is null)
        {
            return new LoginFinishOutcome.Failed("login expired or already used");
        }
        if (callback.Result is not null)
        {
            return new LoginFinishOutcome.Failed($"authorization server ended the interaction: \"{callback.Result}\"");
        }
        if (callback.InteractRef is null || callback.Hash is null)
        {
            return new LoginFinishOutcome.Failed("finish redirect is missing interact_ref or hash");
        }

        var hashInput = new InteractionHashInput(
            pendingLogin.ClientNonce,
            pendingLogin.InteractNonce,
            callback.InteractRef,
            pendingLogin.AuthServerUrl);
        if (!LoginHash.IsValid(hashInput, callback.Hash))
        {
            return new LoginFinishOutcome.Failed("finish redirect hash does not match");
        }

        AuthResponse grant;
        try
        {
            grant = await client.ContinueGrantAsync(
                new AuthRequestArgs { Url = pendingLogin.ContinueUrl, AccessToken = pendingLogin.ContinueAccessToken },
                new GrantContinueBody { InteractRef = callback.InteractRef },
                cancellationToken);
        }
        catch (Exception error)
        {
            return new LoginFinishOutcome.Failed(Describe(error, "grant continuation failed"));
        }

        if (grant.Subject is null)
        {
            return new LoginFinishOutcome.Failed("authorization server did not return a subject");
        }
        var isSubjectVerified = grant.Subject.Sub_ids.Any(subjectId =>
            subjectId.Format == Sub_idsFormat.Uri && subjectId.Id == pendingLogin.WalletAddress);
        if (!isSubjectVerified)
        {
            return new LoginFinishOutcome.Failed($"authorization server did not verify \"{pendingLogin.WalletAddress}\"");
        }
        return new LoginFinishOutcome.Verified(store.CreateSession(pendingLogin.WalletAddress));
    }

    private static string? ToWalletAddressUrl(string rawWalletAddress)
    {
        var trimmed = rawWalletAddress.Trim();
        var paymentPointer = PaymentPointerPattern().Match(trimmed);
        var candidate = paymentPointer.Success ? $"https://{paymentPointer.Groups["rest"].Value}" : trimmed;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
            ? url.ToString()
            : null;
    }

    // This is a PoC for the SDK branch, so log the full exception and show the SDK's message in the page.
    private string Describe(Exception error, string context)
    {
        logger.LogWarning(error, "{Context}", context);
        return $"{context}: {error.GetType().Name}: {error.Message}";
    }
}
