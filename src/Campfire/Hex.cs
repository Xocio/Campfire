using System.Linq;
using System.Security.Cryptography;

namespace Campfire
{
    /// <summary>Lowercase hexadecimal text, as used for ids and file hashes.</summary>
    internal static class Hex
    {
        public static string Of(byte[] bytes)
        {
            return string.Concat(bytes.Select(b => b.ToString("x2")));
        }

        /// <summary><paramref name="byteCount"/> cryptographically random bytes as hexadecimal text.</summary>
        public static string Random(int byteCount)
        {
            var bytes = new byte[byteCount];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            return Of(bytes);
        }
    }
}
