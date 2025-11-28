using System.Security.Cryptography;
using System.Text;

namespace Reef.Helpers
{
    public static class HashHelper
    {
        public static string ComputeDestinationHash(string name, string type, string config)
        {
            var plainText = $"{name}|{type}|{config}";
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(plainText));
            return Convert.ToBase64String(bytes);
        }
    }
}
