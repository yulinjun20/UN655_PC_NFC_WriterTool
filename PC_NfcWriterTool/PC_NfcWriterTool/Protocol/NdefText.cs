using System;
using System.Text;
using System.Text.RegularExpressions;

namespace PC_NfcWriterTool.Protocol
{
    /// <summary>
    /// NDEF text payload for Dll_NfcWriteTextToTag / UART command 0x5d.
    /// Firmware wraps this ASCII as an NDEF Text record; the PC does not build TLV.
    /// </summary>
    public static class NdefText
    {
        public static readonly Regex MaterialNumber = new Regex(@"^\d{9}$", RegexOptions.Compiled);
        public static readonly Regex CardId = new Regex(@"^[A-Za-z0-9]{4}$", RegexOptions.Compiled);

        public static bool IsValidMaterialNumber(string mn)
        {
            return !string.IsNullOrEmpty(mn) && MaterialNumber.IsMatch(mn);
        }

        public static bool IsValidCardId(string id)
        {
            return !string.IsNullOrEmpty(id) && CardId.IsMatch(id);
        }

        /// <summary>
        /// Canonical payload: CRLF line endings as specified for the reader.
        /// Callers that only have LF are normalized here.
        /// </summary>
        public static string Build(string materialNumber, string cardId)
        {
            if (!IsValidMaterialNumber(materialNumber))
            {
                throw new ArgumentException("物料号必须是 9 位数字。", "materialNumber");
            }

            if (!IsValidCardId(cardId))
            {
                throw new ArgumentException("ID号必须是 4 位字母或数字。", "cardId");
            }

            return "MN:" + materialNumber + "\r\nID:" + cardId.ToUpperInvariant() + "\r\n";
        }

        public static byte[] BuildBytes(string materialNumber, string cardId)
        {
            return Encoding.ASCII.GetBytes(Build(materialNumber, cardId));
        }

        public static string NormalizeNewlines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        }

        /// <summary>
        /// Parse MN/ID from tag text. Tolerant of LF or CRLF and ID letter case.
        /// </summary>
        public static bool TryParse(string text, out string materialNumber, out string cardId)
        {
            materialNumber = null;
            cardId = null;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
            Match mn = Regex.Match(normalized, @"MN:(\d{9})\s*(?:\n|$)", RegexOptions.IgnoreCase);
            Match id = Regex.Match(normalized, @"ID:([A-Za-z0-9]{4})\s*(?:\n|$)", RegexOptions.IgnoreCase);
            if (!mn.Success || !id.Success)
            {
                return false;
            }

            materialNumber = mn.Groups[1].Value;
            cardId = id.Groups[1].Value.ToUpperInvariant();
            return true;
        }

        /// <summary>
        /// True when actualText parses to the expected MN/ID (ID compared case-insensitively).
        /// </summary>
        public static bool Matches(string expectedMn, string expectedId, string actualText)
        {
            string mn;
            string id;
            if (!TryParse(actualText, out mn, out id))
            {
                return false;
            }

            if (string.IsNullOrEmpty(expectedMn) || string.IsNullOrEmpty(expectedId))
            {
                return false;
            }

            return string.Equals(mn, expectedMn, StringComparison.Ordinal)
                && string.Equals(id, expectedId, StringComparison.OrdinalIgnoreCase);
        }

    }
}
