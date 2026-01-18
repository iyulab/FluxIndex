namespace FluxIndex.Extensions.FileVault.Domain.ValueObjects;

/// <summary>
/// Immutable value object representing a content hash (SHA256).
/// </summary>
public readonly record struct ContentHash
{
    private readonly string _value;

    public string Value => _value ?? string.Empty;

    private ContentHash(string value)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Creates a ContentHash from a hex string.
    /// </summary>
    public static ContentHash FromHex(string hexValue)
    {
        ArgumentNullException.ThrowIfNull(hexValue);

        if (hexValue.Length != 64)
            throw new ArgumentException("SHA256 hash must be 64 hex characters", nameof(hexValue));

        if (!IsValidHex(hexValue))
            throw new ArgumentException("Invalid hex characters in hash", nameof(hexValue));

        return new ContentHash(hexValue.ToLowerInvariant());
    }

    /// <summary>
    /// Creates a ContentHash from raw bytes.
    /// </summary>
    public static ContentHash FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length != 32)
            throw new ArgumentException("SHA256 hash must be 32 bytes", nameof(bytes));

        return new ContentHash(Convert.ToHexString(bytes).ToLowerInvariant());
    }

    /// <summary>
    /// Empty hash value.
    /// </summary>
    public static ContentHash Empty => new(new string('0', 64));

    /// <summary>
    /// Checks if the hash is empty (all zeros).
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(_value) || _value.All(c => c == '0');

    public override string ToString() => Value;

    private static bool IsValidHex(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c))
                return false;
        }
        return true;
    }
}
