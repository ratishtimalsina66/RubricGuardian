using RubricGuardian.Web.Services;
using Xunit;

namespace RubricGuardian.Web.Tests;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_Then_Verify_RoundTrips()
    {
        var hash = _hasher.Hash("correct-horse-battery-staple");
        Assert.True(_hasher.Verify("correct-horse-battery-staple", hash));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash("correct-horse-battery-staple");
        Assert.False(_hasher.Verify("wrong-password", hash));
    }

    [Fact]
    public void Verify_OldLowerIterationHash_StillVerifies()
    {
        // Simulates a hash created before the iteration count was bumped from 100k to 600k.
        // Verify() reads the iteration count from the stored hash string itself, so old
        // hashes must keep working even after the constant changes for new hashes.
        var salt = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        var key = Convert.ToBase64String(
            System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
                "legacy-password",
                Convert.FromBase64String(salt),
                100_000,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                32));
        var legacyHash = $"100000.{salt}.{key}";

        Assert.True(_hasher.Verify("legacy-password", legacyHash));
        Assert.False(_hasher.Verify("wrong-password", legacyHash));
    }
}
