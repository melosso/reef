using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Hybrid RSA+AES encryption service for securing sensitive data
/// - RSA 2048-bit for key encryption
/// - AES 256-bit for data encryption
/// - Encrypted content prefix: "PWENC:"
/// </summary>
public class EncryptionService
{
    private const string EncryptedHeader = "PWENC:";
    private const string PrivateKeyFileName = "recovery.baklz4";
    private const string PublicKeyFileName = "snapshot_blob.bin";
    private readonly string _certsPath;
    private string _currentPublicKeyPem = string.Empty;

    // Fallback key - only used if no environment variable is set
    private const string FallbackKey = "$REEF2.0_FallbackEncryptionKey_ChangeInProduction_MinLength32Chars#";

    private readonly string _encryptionKey;

    public EncryptionService(string rootPath)
    {
        // Find the project root by searching upwards
        var projectRoot = FindProjectRoot(rootPath);
        _certsPath = Path.Combine(projectRoot, ".core");
        Directory.CreateDirectory(_certsPath);

        // Hide .core directory on Windows
        if (OperatingSystem.IsWindows())
        {
            var dirInfo = new DirectoryInfo(_certsPath);
            if ((dirInfo.Attributes & FileAttributes.Hidden) == 0)
            {
                dirInfo.Attributes |= FileAttributes.Hidden;
            }
        }

        // Load encryption key from environment variable or .env file
        _encryptionKey = LoadEncryptionKey();

        InitializeKeyPair();
    }

    /// <summary>
    /// Encrypt plaintext using hybrid RSA+AES encryption
    /// </summary>
    public string Encrypt(string plainText)
    {
        return Encrypt(plainText, _currentPublicKeyPem);
    }

    /// <summary>
    /// Decrypt encrypted content using private key
    /// </summary>
    public string Decrypt(string encryptedContent)
    {

        // If content is not encrypted, validate it is not empty or whitespace
        if (!IsEncrypted(encryptedContent))
        {
            if (string.IsNullOrWhiteSpace(encryptedContent))
            {
                throw new ArgumentException("Connection string is empty or whitespace. Cannot proceed.");
            }
            // Validate for SQL Server connection string format
            if (!(encryptedContent.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
                  encryptedContent.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Connection string does not appear valid for SQL Server: '{encryptedContent}'");
            }
            return encryptedContent;
        }

        var privateKeyPath = Path.Combine(_certsPath, PrivateKeyFileName);
        if (!File.Exists(privateKeyPath))
        {
            throw new InvalidOperationException("Private key not found. Required for decryption.");
        }

        var encrypted = File.ReadAllText(privateKeyPath);
        var privateKey = DecryptPrivateKey(encrypted);
        return Decrypt(encryptedContent, privateKey);
    }

    /// <summary>
    /// Check if content is encrypted
    /// </summary>
    public bool IsEncrypted(string content)
    {
        return content.StartsWith(EncryptedHeader);
    }

    /// <summary>
    /// Load encryption key from various sources (priority order)
    /// </summary>
    private string LoadEncryptionKey()
    {
        // Priority 1: Check Windows environment variable
        var envKey = Environment.GetEnvironmentVariable("REEF_ENCRYPTION_KEY", EnvironmentVariableTarget.Machine);
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            Log.Debug("Using encryption key from Windows system environment variable");
            return envKey;
        }

        // Priority 2: Check process environment variable
        envKey = Environment.GetEnvironmentVariable("REEF_ENCRYPTION_KEY", EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            Log.Debug("Using encryption key from process environment variable");
            return envKey;
        }

        // Priority 3: Check .env file (for Docker/development)
        var projectRoot = FindProjectRoot(AppContext.BaseDirectory);
        var envFilePath = Path.Combine(projectRoot, ".env");

        if (File.Exists(envFilePath))
        {
            try
            {
                var envLines = File.ReadAllLines(envFilePath);
                foreach (var line in envLines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    var parts = trimmed.Split('=', 2);
                    if (parts.Length == 2 && parts[0].Trim() == "REEF_ENCRYPTION_KEY")
                    {
                        var key = parts[1].Trim().Trim('"', '\'');
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            Log.Debug("Using encryption key from .env file");
                            return key;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to read environment file at {Path}", envFilePath);
            }
        }

        // Priority 4: Fallback to hardcoded key (with warning)
        Log.Warning("! No REEF_ENCRYPTION_KEY found in environment or .env file. Using fallback key.");
        Log.Warning("! For production, set REEF_ENCRYPTION_KEY environment variable or create .env file.");
        Log.Information("");

        return FallbackKey;
    }

    /// <summary>
    /// Find project root by looking for .env file or Reef.db
    /// </summary>
    public static string FindProjectRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, ".env")) ||
                File.Exists(Path.Combine(current.FullName, "Reef.db")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        // Fallback to startPath if not found
        return startPath;
    }

    /// <summary>
    /// Initialize or load RSA key pair
    /// </summary>
    private void InitializeKeyPair()
    {
        var privateKeyPath = Path.Combine(_certsPath, PrivateKeyFileName);
        var publicKeyPath = Path.Combine(_certsPath, PublicKeyFileName);

        if (!File.Exists(privateKeyPath))
        {
            Log.Debug("Private key not found. Generating new keypair...");

            // Generate new keypair
            using var rsa = RSA.Create(2048);
            var privateKeyPem = ExportPrivateKeyPem(rsa);
            var publicKeyPem = ExportPublicKeyPem(rsa);

            // Save private key (encrypted with master encryption key)
            File.WriteAllText(privateKeyPath, EncryptPrivateKey(privateKeyPem));
            Log.Debug("Private key saved to: {PrivateKeyPath}", privateKeyPath);

            // Save public key
            File.WriteAllText(publicKeyPath, publicKeyPem);
            Log.Debug("Public key saved to: {PublicKeyPath}", publicKeyPath);

            // Save reference file
            var referencePath = Path.Combine(_certsPath, "store.jsonc");
            var machine = Environment.MachineName;
            var timestamp = DateTimeOffset.Now.ToString("o"); // ISO 8601

            var referenceContent = new
            {
                MachineIdentity = Convert.ToBase64String(Encoding.UTF8.GetBytes(machine)),
                Timestamp = timestamp
            };
            File.WriteAllText(referencePath, JsonSerializer.Serialize(referenceContent, new JsonSerializerOptions { WriteIndented = true }));
            Log.Debug("Reference file saved to: {ReferencePath}", referencePath);

            // Update current public key
            _currentPublicKeyPem = publicKeyPem;
            Log.Information("! Generated new RSA keypair for encryption");
        }
        else
        {
            // Private key exists, derive public key from it
            try
            {
                var encrypted = File.ReadAllText(privateKeyPath);
                var privateKeyPem = DecryptPrivateKey(encrypted);
                using var rsa = RSA.Create();
                rsa.ImportFromPem(privateKeyPem);
                var derivedPublicKeyPem = ExportPublicKeyPem(rsa);
                _currentPublicKeyPem = derivedPublicKeyPem;

                // Also save/update public key file
                File.WriteAllText(publicKeyPath, derivedPublicKeyPem);

                Log.Debug("Loaded existing private key and derived public key");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load private key. The encryption key may have changed.");
                throw new InvalidOperationException(
                    "Failed to decrypt private key. If you changed REEF_ENCRYPTION_KEY, " +
                    "you must delete the .core folder to regenerate keys.", ex);
            }
        }
    }

    /// <summary>
    /// AES + RSA hybrid encryption
    /// </summary>
    private static string Encrypt(string plainText, string publicKeyPem)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] cipherBytes;
        using (var ms = new MemoryStream())
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            cs.Write(plainBytes, 0, plainBytes.Length);
            cs.FlushFinalBlock();
            cipherBytes = ms.ToArray();
        }

        var keyIv = new byte[aes.Key.Length + aes.IV.Length];
        Buffer.BlockCopy(aes.Key, 0, keyIv, 0, aes.Key.Length);
        Buffer.BlockCopy(aes.IV, 0, keyIv, aes.Key.Length, aes.IV.Length);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        var encryptedKeyIv = rsa.Encrypt(keyIv, RSAEncryptionPadding.OaepSHA256);

        return EncryptedHeader + Convert.ToBase64String(encryptedKeyIv) + "::" + Convert.ToBase64String(cipherBytes);
    }

    /// <summary>
    /// AES + RSA hybrid decryption
    /// </summary>
    private static string Decrypt(string encryptedContent, string privateKeyPem)
    {
        if (!encryptedContent.StartsWith(EncryptedHeader))
            throw new InvalidOperationException("Content is not encrypted");

        var payload = encryptedContent.Substring(EncryptedHeader.Length);
        var parts = payload.Split(new[] { "::" }, StringSplitOptions.None);
        if (parts.Length != 2)
            throw new FormatException("Invalid encrypted format");

        var encryptedKeyIv = Convert.FromBase64String(parts[0]);
        var cipherBytes = Convert.FromBase64String(parts[1]);

        var sanitizedPem = string.Join("\n",
            privateKeyPem.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()));

        using var rsa = RSA.Create();
        rsa.ImportFromPem(sanitizedPem);
        var keyIv = rsa.Decrypt(encryptedKeyIv, RSAEncryptionPadding.OaepSHA256);

        var key = new byte[32];
        var iv = new byte[16];
        Buffer.BlockCopy(keyIv, 0, key, 0, 32);
        Buffer.BlockCopy(keyIv, 32, iv, 0, 16);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        using var ms = new MemoryStream(cipherBytes);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    /// <summary>
    /// Export private key as PEM format
    /// </summary>
    private static string ExportPrivateKeyPem(RSA rsa)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN PRIVATE KEY-----");
        builder.AppendLine(Convert.ToBase64String(rsa.ExportPkcs8PrivateKey(), Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine("-----END PRIVATE KEY-----");
        return builder.ToString();
    }

    /// <summary>
    /// Export public key as PEM format
    /// </summary>
    private static string ExportPublicKeyPem(RSA rsa)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN PUBLIC KEY-----");
        builder.AppendLine(Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo(), Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine("-----END PUBLIC KEY-----");
        return builder.ToString();
    }

    /// <summary>
    /// Encrypt private key using master encryption key
    /// </summary>
    private string EncryptPrivateKey(string pem)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(_encryptionKey.PadRight(32).Substring(0, 32));
        aes.GenerateIV();
        var iv = aes.IV;
        using var ms = new MemoryStream();
        ms.Write(iv, 0, iv.Length);
        using var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
        var bytes = Encoding.UTF8.GetBytes(pem);
        cs.Write(bytes, 0, bytes.Length);
        cs.FlushFinalBlock();
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>
    /// Decrypt private key using master encryption key
    /// </summary>
    private string DecryptPrivateKey(string encrypted)
    {
        var bytes = Convert.FromBase64String(encrypted);
        using var ms = new MemoryStream(bytes);
        var iv = new byte[16];
        ms.Read(iv, 0, 16);
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(_encryptionKey.PadRight(32).Substring(0, 32));
        aes.IV = iv;
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);
        return sr.ReadToEnd();
    }
}
