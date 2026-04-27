// File: Assets/_Project/Core/Netcode/FixedStringUtility.cs
using System.Text;
using Unity.Collections;

namespace HueDoneIt.Core.Netcode
{
    public static class FixedStringUtility
    {
        private const string Ellipsis = "...";

        public static FixedString32Bytes ToFixedString32(string value)
        {
            return new FixedString32Bytes(ClampUtf8(value, 29));
        }

        public static FixedString64Bytes ToFixedString64(string value)
        {
            return new FixedString64Bytes(ClampUtf8(value, 61));
        }

        public static FixedString128Bytes ToFixedString128(string value)
        {
            return new FixedString128Bytes(ClampUtf8(value, 125));
        }

        private static string ClampUtf8(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
            {
                return value;
            }

            int budget = maxBytes - Encoding.UTF8.GetByteCount(Ellipsis);
            if (budget <= 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new();
            int used = 0;
            for (int i = 0; i < value.Length; i++)
            {
                string unit = value[i].ToString();
                int byteCount = Encoding.UTF8.GetByteCount(unit);
                if (used + byteCount > budget)
                {
                    break;
                }

                builder.Append(value[i]);
                used += byteCount;
            }

            builder.Append(Ellipsis);
            return builder.ToString();
        }
    }
}
