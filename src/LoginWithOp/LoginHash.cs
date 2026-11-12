using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace LoginWithOp;

/// <summary>Values the authorization server hashes to sign the finish redirect (GNAP section 4.2.3).</summary>
public sealed record InteractionHashInput(
    string ClientNonce,
    string InteractNonce,
    string InteractRef,
    string AuthServerUrl);

/// <summary>Computes and checks the <c>hash</c> the authorization server adds to the finish redirect.</summary>
public static class LoginHash
{
    /// <summary>The GNAP interaction hash as unpadded base64url.</summary>
    public static string Compute(InteractionHashInput input) => Base64Url.EncodeToString(Digest(input));

    /// <summary>True when <paramref name="receivedHash"/> matches; accepts base64url (GNAP) and padded base64 (Open Payments docs sample).</summary>
    public static bool IsValid(InteractionHashInput input, string receivedHash)
    {
        var digest = Digest(input);
        return new[] { Base64Url.EncodeToString(digest), Convert.ToBase64String(digest) }
            .Any(expected => IsEqual(expected, receivedHash));
    }

    private static byte[] Digest(InteractionHashInput input)
    {
        var hashBase = string.Join('\n', input.ClientNonce, input.InteractNonce, input.InteractRef, input.AuthServerUrl);
        return SHA256.HashData(Encoding.UTF8.GetBytes(hashBase));
    }

    private static bool IsEqual(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
