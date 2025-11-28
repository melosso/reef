using System.Text.Json;
using System.Text.Json.Nodes;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Handles selective encryption of sensitive fields within destination configurations
/// Ensures secrets like passwords, tokens, and keys are encrypted at rest
/// </summary>
public class DestinationConfigEncryption
{
    private readonly EncryptionService _encryption;
    private const string MaskedValue = "[SECRET]";

    // Define which fields should be encrypted for each destination type
    // IMPORTANT: All keys must be lowercase to match ToLowerInvariant() comparison
    private static readonly Dictionary<DestinationType, HashSet<string>> SecretFields = new()
    {
        [DestinationType.Http] = new() { "authtoken", "password", "username" },
        [DestinationType.S3] = new() { "accesskey", "secretkey" },
        [DestinationType.AzureBlob] = new() { "connectionstring" },
        [DestinationType.Ftp] = new() { "password", "username" },
        [DestinationType.Sftp] = new() { "password", "username", "privatekeypath" },
        [DestinationType.Email] = new() { "smtppassword", "smtpusername", "password", "resendapikey", "sendgridapikey", "oauthtoken" },
        [DestinationType.NetworkShare] = new() { "password", "username" },
        [DestinationType.Local] = new() { }, // No secrets for local
        [DestinationType.WebDav] = new() { "password", "username" }
    };

    public DestinationConfigEncryption(EncryptionService encryption)
    {
        _encryption = encryption;
    }

    /// <summary>
    /// Encrypt secret fields within a destination configuration JSON
    /// Only encrypts fields that are marked as secrets for the given destination type
    /// </summary>
    public string EncryptSecretFields(string configJson, DestinationType type)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return configJson;

        try
        {
            Log.Debug("Encrypting secret fields for {DestinationType}, config length: {Length}", type, configJson.Length);

            var jsonNode = JsonNode.Parse(configJson);
            if (jsonNode == null)
            {
                Log.Warning("JSON parsing returned null for {DestinationType}", type);
                return configJson;
            }

            var secretFieldNames = GetSecretFields(type);
            Log.Debug("Secret fields for {DestinationType}: {Fields}", type, string.Join(", ", secretFieldNames));

            EncryptFieldsRecursive(jsonNode, secretFieldNames);

            var result = jsonNode.ToJsonString();
            Log.Debug("Encrypted config length: {Length}", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to encrypt secret fields for {DestinationType}. Config: {Config}", type, configJson);
            return configJson;
        }
    }

    /// <summary>
    /// Decrypt secret fields within a destination configuration JSON
    /// </summary>
    public string DecryptSecretFields(string configJson, DestinationType type)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return configJson;

        try
        {
            var jsonNode = JsonNode.Parse(configJson);
            if (jsonNode == null)
                return configJson;

            var secretFieldNames = GetSecretFields(type);
            DecryptFieldsRecursive(jsonNode, secretFieldNames);

            return jsonNode.ToJsonString();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to decrypt secret fields for {DestinationType}", type);
            return configJson;
        }
    }

    /// <summary>
    /// Mask secret fields for display in the UI
    /// Replaces secret values with a placeholder to prevent exposure
    /// </summary>
    public string MaskSecretFields(string configJson, DestinationType type)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return configJson;

        try
        {
            Log.Debug("Masking secret fields for {DestinationType}", type);

            var jsonNode = JsonNode.Parse(configJson);
            if (jsonNode == null)
            {
                Log.Warning("JSON parsing returned null while masking for {DestinationType}", type);
                return configJson;
            }

            var secretFieldNames = GetSecretFields(type);
            MaskFieldsRecursive(jsonNode, secretFieldNames);

            return jsonNode.ToJsonString();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to mask secret fields for {DestinationType}. Config: {Config}", type, configJson);
            return configJson;
        }
    }

    /// <summary>
    /// Merge incoming configuration from UI with existing encrypted configuration
    /// Only updates fields that have changed (not masked values)
    /// </summary>
    public string MergeWithExisting(string incomingConfigJson, string existingConfigJson, DestinationType type)
    {
        if (string.IsNullOrWhiteSpace(existingConfigJson))
            return incomingConfigJson;

        if (string.IsNullOrWhiteSpace(incomingConfigJson))
            return existingConfigJson;

        try
        {
            var incoming = JsonNode.Parse(incomingConfigJson);
            var existing = JsonNode.Parse(existingConfigJson);

            if (incoming == null || existing == null)
                return incomingConfigJson;

            var secretFieldNames = GetSecretFields(type);
            MergeFieldsRecursive(incoming, existing, secretFieldNames);

            return incoming.ToJsonString();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to merge configurations for {DestinationType}", type);
            return incomingConfigJson;
        }
    }

    private HashSet<string> GetSecretFields(DestinationType type)
    {
        return SecretFields.GetValueOrDefault(type, new HashSet<string>());
    }

    private void EncryptFieldsRecursive(JsonNode? node, HashSet<string> secretFields)
    {
        if (node is JsonObject obj)
        {
            foreach (var kvp in obj.ToList())
            {
                var key = kvp.Key;
                var value = kvp.Value;

                // Check if this field is a secret (case-insensitive)
                if (secretFields.Contains(key.ToLowerInvariant()) && value is JsonValue jsonValue)
                {
                    try
                    {
                        // Try to get as string, skip if not a string
                        if (jsonValue.TryGetValue<string>(out var stringValue))
                        {
                            if (!string.IsNullOrWhiteSpace(stringValue) && !_encryption.IsEncrypted(stringValue))
                            {
                                // Encrypt the value
                                obj[key] = _encryption.Encrypt(stringValue);
                                Log.Debug("Encrypted field: {Field}", key);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Could not encrypt field {Field}, skipping", key);
                    }
                }
                else if (value is JsonObject || value is JsonArray)
                {
                    // Recurse into nested objects/arrays
                    EncryptFieldsRecursive(value, secretFields);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                EncryptFieldsRecursive(item, secretFields);
            }
        }
    }

    private void DecryptFieldsRecursive(JsonNode? node, HashSet<string> secretFields)
    {
        if (node is JsonObject obj)
        {
            foreach (var kvp in obj.ToList())
            {
                var key = kvp.Key;
                var value = kvp.Value;

                // Check if this field is a secret (case-insensitive)
                if (secretFields.Contains(key.ToLowerInvariant()) && value is JsonValue jsonValue)
                {
                    try
                    {
                        // Try to get as string, skip if not a string
                        if (jsonValue.TryGetValue<string>(out var stringValue))
                        {
                            if (!string.IsNullOrWhiteSpace(stringValue) && _encryption.IsEncrypted(stringValue))
                            {
                                // Decrypt the value
                                obj[key] = _encryption.Decrypt(stringValue);
                                Log.Debug("Decrypted field: {Field}", key);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Could not decrypt field {Field}, skipping", key);
                    }
                }
                else if (value is JsonObject || value is JsonArray)
                {
                    // Recurse into nested objects/arrays
                    DecryptFieldsRecursive(value, secretFields);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                DecryptFieldsRecursive(item, secretFields);
            }
        }
    }

    private void MaskFieldsRecursive(JsonNode? node, HashSet<string> secretFields)
    {
        if (node is JsonObject obj)
        {
            foreach (var kvp in obj.ToList())
            {
                var key = kvp.Key;
                var value = kvp.Value;

                // Check if this field is a secret (case-insensitive)
                if (secretFields.Contains(key.ToLowerInvariant()))
                {
                    try
                    {
                        // Always mask secret fields, regardless of their current value
                        // This way the frontend knows the field exists but shouldn't see its value
                        if (value is JsonValue jsonValue)
                        {
                            obj[key] = MaskedValue;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Could not mask field {Field}, skipping", key);
                    }
                }
                else if (value is JsonObject || value is JsonArray)
                {
                    // Recurse into nested objects/arrays
                    MaskFieldsRecursive(value, secretFields);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                MaskFieldsRecursive(item, secretFields);
            }
        }
    }

    private void MergeFieldsRecursive(JsonNode? incoming, JsonNode? existing, HashSet<string> secretFields)
    {
        if (incoming is JsonObject incomingObj && existing is JsonObject existingObj)
        {
            foreach (var kvp in incomingObj.ToList())
            {
                var key = kvp.Key;
                var incomingValue = kvp.Value;

                // Check if this field is a secret
                if (secretFields.Contains(key.ToLowerInvariant()) && incomingValue is JsonValue jsonValue)
                {
                    try
                    {
                        // Try to get as string
                        if (jsonValue.TryGetValue<string>(out var stringValue))
                        {
                            // If the incoming value is the masked placeholder, use the existing encrypted value
                            if (stringValue == MaskedValue && existingObj.ContainsKey(key))
                            {
                                incomingObj[key] = existingObj[key]?.DeepClone();
                                Log.Debug("Preserved existing encrypted value for field: {Field}", key);
                            }
                            // If it's a new value (not masked), it will be encrypted later
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Could not merge field {Field}, keeping incoming value", key);
                    }
                }
                else if (incomingValue is JsonObject && existingObj.ContainsKey(key) && existingObj[key] is JsonObject)
                {
                    // Recurse into nested objects
                    MergeFieldsRecursive(incomingValue, existingObj[key], secretFields);
                }
            }
        }
    }
}
