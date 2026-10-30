using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Campfire.Link
{
    /// <summary>
    /// Invite code of a room, written as <c>U/XXXX-XXXX-XXXX-XXXX</c>.
    /// </summary>
    /// <remarks>
    /// The 16 characters are the base-34 digits of a number that is a multiple of 7, least significant first.
    /// The first eight digits name the virtual network and the last eight are its secret.
    /// </remarks>
    internal sealed class RoomCode
    {
        private const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string NetworkPrefix = "scaffolding-mc-";
        private const int DigitCount = 16;
        private const int Checksum = 7;

        /// <summary>Length of "U/XXXX-XXXX-XXXX-XXXX".</summary>
        private const int TextLength = 21;

        private static readonly BigInteger Range = BigInteger.Pow(Alphabet.Length, DigitCount);

        private RoomCode(BigInteger value)
        {
            var digits = new char[DigitCount];
            for (int i = 0; i < DigitCount; i++)
            {
                digits[i] = Alphabet[(int)(value % Alphabet.Length)];
                value /= Alphabet.Length;
            }

            var code = new StringBuilder("U/", TextLength);
            for (int i = 0; i < DigitCount; i++)
            {
                if (i > 0 && i % 4 == 0)
                    code.Append('-');
                code.Append(digits[i]);
            }

            Code = code.ToString();
            NetworkName = NetworkPrefix + Group(digits, 0) + "-" + Group(digits, 4);
            NetworkSecret = Group(digits, 8) + "-" + Group(digits, 12);
        }

        /// <summary>The code shown to players, e.g. <c>U/1A2B-3C4D-5E6F-7G8H</c>.</summary>
        public string Code { get; }

        public string NetworkName { get; }

        public string NetworkSecret { get; }

        public override string ToString()
        {
            return Code;
        }

        /// <summary>Creates a random code for a new room.</summary>
        public static RoomCode Create()
        {
            // The extra zero byte keeps the little-endian value positive.
            var bytes = new byte[17];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes, 0, 16);

            var value = new BigInteger(bytes) % Range;
            return new RoomCode(value - value % Checksum);
        }

        /// <summary>
        /// Finds the first valid code in <paramref name="text"/>, which may be a whole chat message.
        /// Case is ignored and the look-alike letters I and O are read as 1 and 0.
        /// </summary>
        public static RoomCode Find(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            string upper = text.ToUpperInvariant();
            for (int start = 0; start + TextLength <= upper.Length; start++)
            {
                if (upper[start] == 'U' && upper[start + 1] == '/' && TryDecode(upper, start + 2, out var value))
                    return new RoomCode(value);
            }
            return null;
        }

        private static bool TryDecode(string text, int offset, out BigInteger value)
        {
            value = BigInteger.Zero;

            // The body "XXXX-XXXX-XXXX-XXXX" ends with the most significant digit.
            for (int i = TextLength - 3; i >= 0; i--)
            {
                char c = text[offset + i];
                if (i == 4 || i == 9 || i == 14)
                {
                    if (c != '-')
                        return false;
                    continue;
                }

                int digit = DigitOf(c);
                if (digit < 0)
                    return false;
                value = value * Alphabet.Length + digit;
            }
            return value % Checksum == 0;
        }

        private static int DigitOf(char c)
        {
            if (c == 'I')
                c = '1';
            else if (c == 'O')
                c = '0';
            return Alphabet.IndexOf(c);
        }

        private static string Group(char[] digits, int start)
        {
            return new string(digits, start, 4);
        }
    }
}
