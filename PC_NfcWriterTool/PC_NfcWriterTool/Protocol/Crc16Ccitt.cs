using System;

namespace PC_NfcWriterTool.Protocol
{
    /// <summary>
    /// CRC used by MH2020C / UN637 pinpad host UART.
    /// Firmware <c>SendingCommand()</c> in pinpad/commandpro.c:
    /// <code>
    /// Crc16CCITT((BYTE*)cmdBuf, cmdLen+4, crc);
    /// memcpy(cmdBuf+cmdLen+4, crc, 2);
    /// </code>
    /// The public sibling repo does not ship Crc16CCITT source (it lives in the
    /// closed Landi-style lib). Named CCITT helpers in this SDK family are
    /// CRC-16/XMODEM: poly 0x1021, init 0x0000, refin=false, refout=false,
    /// xorout 0x0000, CRC emitted high-byte first.
    /// </summary>
    public static class Crc16Ccitt
    {
        public const ushort Polynomial = 0x1021;
        public const ushort InitXModem = 0x0000;
        public const ushort InitCcittFalse = 0xFFFF;

        public static byte[] Compute(byte[] data, int offset, int count, ushort init)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            ushort crc = init;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((crc & 0x8000) != 0)
                    {
                        crc = (ushort)((crc << 1) ^ Polynomial);
                    }
                    else
                    {
                        crc = (ushort)(crc << 1);
                    }
                }
            }

            return new byte[] { (byte)(crc >> 8), (byte)(crc & 0xFF) };
        }

        public static byte[] ComputeXModem(byte[] data, int offset, int count)
        {
            return Compute(data, offset, count, InitXModem);
        }

        public static bool Matches(byte[] data, int offset, int count, byte crcHi, byte crcLo, ushort init)
        {
            byte[] expected = Compute(data, offset, count, init);
            return expected[0] == crcHi && expected[1] == crcLo;
        }
    }
}
