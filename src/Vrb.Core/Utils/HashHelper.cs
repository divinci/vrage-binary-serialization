using System.IO;
using System.Security.Cryptography;

namespace Vrb.Utils;

/// <summary>
/// Utility class for computing cryptographic hashes of files and byte arrays.
/// Used for validation and binary fidelity verification.
/// </summary>
public static class HashHelper
{
    /// <summary>
    /// Computes the SHA256 hash of a file.
    /// </summary>
    /// <param name="filePath">Path to the file to hash.</param>
    /// <returns>Lowercase hexadecimal string representation of the hash.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    public static string ComputeFileHash(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = sha256.ComputeHash(stream);
        return BytesToHex(hashBytes);
    }

    /// <summary>
    /// Computes the SHA256 hash of a byte array.
    /// </summary>
    /// <param name="data">The byte array to hash.</param>
    /// <returns>Lowercase hexadecimal string representation of the hash.</returns>
    public static string ComputeHash(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(data);
        return BytesToHex(hashBytes);
    }

    /// <summary>
    /// Computes the SHA256 hash of a stream.
    /// </summary>
    /// <param name="stream">The stream to hash.</param>
    /// <returns>Lowercase hexadecimal string representation of the hash.</returns>
    public static string ComputeStreamHash(Stream stream)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(stream);
        return BytesToHex(hashBytes);
    }

    /// <summary>
    /// Converts a byte array to a lowercase hexadecimal string.
    /// </summary>
    private static string BytesToHex(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}

