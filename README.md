# Login with Open Payments, .NET (PoC)

.NET port of [login-with-op](../login-with-op), used to check that the `cozmin/net-27` branch of [open-payments-dotnet](https://github.com/interledger/open-payments-dotnet) can do wallet ownership login against the test wallet.

The user enters a wallet address (`https://ilp.interledger-test.dev/alice` or `$ilp.interledger-test.dev/alice`). The app requests an interactive grant with a `subject` and no `access_token`, then sends the user to their wallet to approve it. On the way back it checks the `hash`, continues the grant, and opens a session if `subject.sub_ids` includes the wallet address. This is the flow from the "Verify ownership of a wallet address" guide on openpayments.dev.

## Run

You need the .NET 9 SDK and a wallet address for the app itself with a developer key (test wallet: Settings > Developer Keys).

```sh
git submodule update --init --recursive
cp .env.example .env   # fill in OP_WALLET_ADDRESS, OP_KEY_ID, OP_PRIVATE_KEY
dotnet run --project src/LoginWithOp   # http://localhost:3000
```

The app reads the nearest `.env` it finds walking up from the working directory. A relative `OP_PRIVATE_KEY` path resolves against that `.env`'s folder, so the Node project's `.env` and `private.key` work unchanged. Real env vars override `.env`, and flags override both (`dotnet run --project src/LoginWithOp -- --help`).

## Layout

- `lib/open-payments-dotnet`: SDK submodule on `cozmin/net-27`, referenced as a project, not from NuGet
- `src/LoginWithOp/LoginProvider.cs`: grant request, hash check, continuation, subject check
- `src/LoginWithOp/LoginHash.cs`: GNAP interaction hash (the SDK doesn't have a helper for this)
- `src/LoginWithOp/LoginStore.cs`: pending logins and sessions in memory, with TTLs
- `src/LoginWithOp/LoginConfig.cs`: flags, env and `.env` loading
- `src/LoginWithOp/Program.cs`: minimal API with `/`, `POST /login`, `GET /callback`, `POST /logout`

## PoC limits

- No automated tests. It's checked by logging in manually against the test wallet.
- State lives in memory, so a restart logs everyone out.
- No CSRF tokens. It relies on `SameSite=Lax` cookies.
- The pending login is tied to the browser with an `op_login` cookie, so a finish redirect can't be replayed or finished in a different browser.
- SDK errors are logged in full and shown on the error page, to make problems with the branch easy to see.
