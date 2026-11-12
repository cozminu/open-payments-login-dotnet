namespace LoginWithOp;

/// <summary>Settings from flags, then environment, then a <c>.env</c> file in the working directory.</summary>
public sealed record LoginConfig(
    string WalletAddress,
    string KeyId,
    string PrivateKeyPem,
    int Port,
    Uri? PublicUrl)
{
    public const int DefaultPort = 3000;

    public const string Usage =
        """
        usage: dotnet run --project src/LoginWithOp -- --wallet-address <url> --key-id <id> --private-key <path|pem>
                                                       [--port <n>] [--public-url <url>]
        env: OP_WALLET_ADDRESS, OP_KEY_ID, OP_PRIVATE_KEY, PORT, PUBLIC_URL (also read from the nearest .env)
        """;

    private static readonly Dictionary<string, string> FlagToEnv = new()
    {
        ["--wallet-address"] = "OP_WALLET_ADDRESS",
        ["--key-id"] = "OP_KEY_ID",
        ["--private-key"] = "OP_PRIVATE_KEY",
        ["--port"] = "PORT",
        ["--public-url"] = "PUBLIC_URL",
    };

    /// <summary>Returns null when <c>--help</c> was passed. Throws <see cref="ArgumentException"/> on bad input.</summary>
    public static LoginConfig? Load(string[] args, string startDirectory)
    {
        var flags = ParseFlags(args);
        if (flags is null)
        {
            return null;
        }
        var dotEnvPath = FindDotEnv(startDirectory);
        var dotEnv = dotEnvPath is null ? [] : ParseDotEnv(File.ReadAllLines(dotEnvPath));
        // Relative key paths in .env are relative to the .env file, so it works from any directory.
        var baseDirectory = dotEnvPath is null ? startDirectory : Path.GetDirectoryName(dotEnvPath)!;

        string? Get(string envName) =>
            flags.GetValueOrDefault(envName)
            ?? NonEmpty(Environment.GetEnvironmentVariable(envName))
            ?? NonEmpty(dotEnv.GetValueOrDefault(envName));

        string Require(string envName) =>
            Get(envName)
            ?? throw new ArgumentException(
                $"missing {FlagToEnv.First(pair => pair.Value == envName).Key} (or {envName})");

        var rawPort = Get("PORT") ?? DefaultPort.ToString();
        if (!int.TryParse(rawPort, out var port) || port is < 1 or > 65_535)
        {
            throw new ArgumentException($"port \"{rawPort}\" is not a valid port");
        }
        var rawPublicUrl = Get("PUBLIC_URL");
        Uri? publicUrl = null;
        if (rawPublicUrl is not null && !Uri.TryCreate(rawPublicUrl, UriKind.Absolute, out publicUrl))
        {
            throw new ArgumentException($"public url \"{rawPublicUrl}\" is not a url");
        }

        return new LoginConfig(
            Require("OP_WALLET_ADDRESS"),
            Require("OP_KEY_ID"),
            ReadPrivateKey(Require("OP_PRIVATE_KEY"), baseDirectory),
            port,
            publicUrl);
    }

    private static Dictionary<string, string>? ParseFlags(string[] args)
    {
        var values = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--help" or "-h")
            {
                return null;
            }
            var (name, value) = arg.Split('=', 2) is [var n, var v] ? (n, v) : (arg, null as string);
            if (!FlagToEnv.TryGetValue(name, out var envName))
            {
                throw new ArgumentException($"unknown option \"{arg}\"");
            }
            value ??= i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{name} needs a value");
            values[envName] = value;
        }
        return values;
    }

    // Enough of the dotenv format for this app: KEY=value, # comments, optional surrounding quotes.
    private static Dictionary<string, string> ParseDotEnv(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>();
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf('=');
            if (line.StartsWith('#') || separator <= 0)
            {
                continue;
            }
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1].Replace("\\n", "\n");
            }
            values[line[..separator].Trim()] = value;
        }
        return values;
    }

    // dotnet run starts the app in the project directory, so look upward for the repo's .env.
    private static string? FindDotEnv(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, ".env");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static string ReadPrivateKey(string pathOrPem, string baseDirectory)
    {
        var pem = pathOrPem.Replace("\\n", "\n");
        if (pem.Contains("-----BEGIN"))
        {
            return pem;
        }
        var path = Path.GetFullPath(pathOrPem, baseDirectory);
        if (!File.Exists(path))
        {
            throw new ArgumentException($"private key file \"{path}\" not found");
        }
        return File.ReadAllText(path);
    }

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
