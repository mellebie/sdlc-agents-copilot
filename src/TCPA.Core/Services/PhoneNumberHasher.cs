using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace TCPA.Core.Services;

public interface IPhoneNumberHasher
{
    /// <summary>Returns HMAC-SHA256 hex digest of the phone number. Deterministic for same key.</summary>
    string Hash(string phoneNumber);
}

public class PhoneNumberHasher : IPhoneNumberHasher
{
    private readonly byte[] _keyBytes;

    public PhoneNumberHasher(IConfiguration configuration)
    {
        var key = configuration["Logging:PhoneHashKey"]
            ?? throw new InvalidOperationException("Logging:PhoneHashKey is not configured. Add it to appsettings or environment variables.");
        _keyBytes = Encoding.UTF8.GetBytes(key);
    }

    public string Hash(string phoneNumber)
    {
        using var hmac = new HMACSHA256(_keyBytes);
        var inputBytes = Encoding.UTF8.GetBytes(phoneNumber ?? string.Empty);
        var hashBytes = hmac.ComputeHash(inputBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
