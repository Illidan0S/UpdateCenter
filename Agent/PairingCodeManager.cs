using System.Security.Cryptography;
using System.Text;
using UpdateCenter.Contracts;

namespace UpdateCenter.Agent;

public sealed class PairingCodeManager
{
    private const int MaximumAttempts = 5;
    private readonly object _sync = new();
    private byte[] _salt = [];
    private byte[] _hash = [];
    private DateTime _expiresUtc;
    private int _attempts;

    public PairingCodeInfo Create()
    {
        var code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8");
        lock (_sync)
        {
            _salt = RandomNumberGenerator.GetBytes(32);
            _hash = Hash(_salt, code);
            _expiresUtc = DateTime.UtcNow.AddMinutes(5);
            _attempts = 0;
            return new PairingCodeInfo { Code = code, ExpiresUtc = _expiresUtc };
        }
    }

    public bool TryConsume(string code)
    {
        lock (_sync)
        {
            if (_hash.Length == 0 || DateTime.UtcNow > _expiresUtc || _attempts >= MaximumAttempts)
            {
                ClearLocked();
                return false;
            }

            _attempts++;
            var candidate = Hash(_salt, code ?? "");
            if (!CryptographicOperations.FixedTimeEquals(candidate, _hash))
            {
                if (_attempts >= MaximumAttempts) ClearLocked();
                return false;
            }

            ClearLocked();
            return true;
        }
    }

    public void Clear()
    {
        lock (_sync) ClearLocked();
    }

    private static byte[] Hash(byte[] salt, string code)
    {
        var codeBytes = Encoding.UTF8.GetBytes(code);
        var input = new byte[salt.Length + codeBytes.Length];
        Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
        Buffer.BlockCopy(codeBytes, 0, input, salt.Length, codeBytes.Length);
        return SHA256.HashData(input);
    }

    private void ClearLocked()
    {
        if (_salt.Length > 0) CryptographicOperations.ZeroMemory(_salt);
        if (_hash.Length > 0) CryptographicOperations.ZeroMemory(_hash);
        _salt = [];
        _hash = [];
        _expiresUtc = default;
        _attempts = 0;
    }
}
