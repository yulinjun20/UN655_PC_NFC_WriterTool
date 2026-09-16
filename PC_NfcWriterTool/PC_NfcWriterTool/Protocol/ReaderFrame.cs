using System;
using System.Collections.Generic;

namespace PC_NfcWriterTool.Protocol
{
    public sealed class HostFrame
    {
        public byte Module { get; set; }
        public byte SubCommand { get; set; }
        public byte[] Payload { get; set; }

        public ushort StatusWord
        {
            get
            {
                if (Payload == null || Payload.Length < 2)
                {
                    return 0xFFFF;
                }

                return (ushort)((Payload[0] << 8) | Payload[1]);
            }
        }

        public bool IsSuccess
        {
            get { return Payload != null && Payload.Length >= 2 && StatusWord == 0; }
        }

        public byte[] StatusData
        {
            get
            {
                if (Payload == null || Payload.Length <= 2)
                {
                    return new byte[0];
                }

                byte[] extra = new byte[Payload.Length - 2];
                Buffer.BlockCopy(Payload, 2, extra, 0, extra.Length);
                return extra;
            }
        }
    }

    public sealed class FrameParseResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
        public HostFrame Frame { get; set; }
        public int Consumed { get; set; }
    }

    /// <summary>
    /// UART framing matching pinpad/commandpro.c SendingCommand / WaitingCommand.
    ///
    /// Wire format:
    ///   STX (0x02)
    ///   cmdBuf[0]  module   (PICC = 0xBA)
    ///   cmdBuf[1]  sub-command
    ///   cmdBuf[2]  LEN high
    ///   cmdBuf[3]  LEN low     LEN = payload bytes only
    ///   cmdBuf[4 .. 4+LEN) payload
    ///   CRC16-CCITT/XMODEM high, low   covering cmdBuf[0 .. 4+LEN)
    ///
    /// STX is not included in CRC. No ETX, no ACK byte in this protocol.
    /// Response: same layout; Picc_Pro typically sets sub-command = request+1
    /// and payload[0..1] = HI_BYTE/LOW_BYTE of abs(Dll_* return). 0 = OK.
    /// </summary>
    public static class ReaderFrame
    {
        public const int HeaderSize = 4;
        public const int CrcSize = 2;
        public const int MaxPayload = 3000;

        public static byte[] Build(byte module, byte subCommand, byte[] payload)
        {
            if (payload == null)
            {
                payload = new byte[0];
            }

            if (payload.Length > MaxPayload)
            {
                throw new ArgumentOutOfRangeException("payload", "Payload exceeds CMD_MAX_BUFLEN.");
            }

            int cmdLen = payload.Length;
            byte[] cmdBuf = new byte[HeaderSize + cmdLen];
            cmdBuf[0] = module;
            cmdBuf[1] = subCommand;
            cmdBuf[2] = (byte)((cmdLen >> 8) & 0xFF);
            cmdBuf[3] = (byte)(cmdLen & 0xFF);
            if (cmdLen > 0)
            {
                Buffer.BlockCopy(payload, 0, cmdBuf, 4, cmdLen);
            }

            byte[] crc = Crc16Ccitt.ComputeXModem(cmdBuf, 0, cmdBuf.Length);
            byte[] wire = new byte[1 + cmdBuf.Length + CrcSize];
            wire[0] = PiccOpcodes.Stx;
            Buffer.BlockCopy(cmdBuf, 0, wire, 1, cmdBuf.Length);
            wire[1 + cmdBuf.Length] = crc[0];
            wire[1 + cmdBuf.Length + 1] = crc[1];
            return wire;
        }

        public static byte[] BuildPicc(byte subCommand, byte[] payload)
        {
            return Build(PiccOpcodes.PiccModule, subCommand, payload);
        }

        /// <summary>
        /// Scan a receive buffer for a complete STX-framed packet.
        /// Returns Consumed so the caller can drop leading junk / the finished frame.
        /// </summary>
        public static FrameParseResult TryParse(byte[] buffer, int offset, int count)
        {
            var result = new FrameParseResult();
            if (buffer == null || count <= 0)
            {
                result.Error = "空缓冲区";
                return result;
            }

            int end = offset + count;
            int stx = -1;
            for (int i = offset; i < end; i++)
            {
                if (buffer[i] == PiccOpcodes.Stx)
                {
                    stx = i;
                    break;
                }
            }

            if (stx < 0)
            {
                result.Consumed = count;
                result.Error = "未找到 STX(0x02)";
                return result;
            }

            int afterStx = stx + 1;
            if (end - afterStx < HeaderSize + CrcSize)
            {
                result.Consumed = stx - offset;
                result.Error = "帧不完整";
                return result;
            }

            int len = (buffer[afterStx + 2] << 8) | buffer[afterStx + 3];
            if (len < 0 || len > MaxPayload)
            {
                result.Consumed = (stx - offset) + 1;
                result.Error = "长度非法: " + len;
                return result;
            }

            int frameLen = HeaderSize + len + CrcSize;
            if (end - afterStx < frameLen)
            {
                result.Consumed = stx - offset;
                result.Error = "帧不完整";
                return result;
            }

            byte crcHi = buffer[afterStx + HeaderSize + len];
            byte crcLo = buffer[afterStx + HeaderSize + len + 1];
            if (!Crc16Ccitt.Matches(buffer, afterStx, HeaderSize + len, crcHi, crcLo, Crc16Ccitt.InitXModem))
            {
                // Fall back to CCITT-FALSE in case a firmware build used init 0xFFFF.
                if (!Crc16Ccitt.Matches(buffer, afterStx, HeaderSize + len, crcHi, crcLo, Crc16Ccitt.InitCcittFalse))
                {
                    result.Consumed = (stx - offset) + 1;
                    result.Error = "CRC 校验失败";
                    return result;
                }
            }

            byte[] payload = new byte[len];
            if (len > 0)
            {
                Buffer.BlockCopy(buffer, afterStx + HeaderSize, payload, 0, len);
            }

            result.Ok = true;
            result.Consumed = (stx - offset) + 1 + frameLen;
            result.Frame = new HostFrame
            {
                Module = buffer[afterStx],
                SubCommand = buffer[afterStx + 1],
                Payload = payload
            };
            return result;
        }

        public static string ToHex(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            char[] hex = new char[data.Length * 2];
            const string digits = "0123456789ABCDEF";
            for (int i = 0; i < data.Length; i++)
            {
                hex[i * 2] = digits[data[i] >> 4];
                hex[i * 2 + 1] = digits[data[i] & 0x0F];
            }

            return new string(hex);
        }

        /// <summary>
        /// Parse Dll_PiccCheck success payload after the 2-byte status:
        /// cardtype[2] + serialno[0]=uidLen + serialno[1..uidLen].
        /// </summary>
        public static bool TryReadPiccCheckUid(byte[] statusData, out string uidHex, out byte[] atqa)
        {
            uidHex = null;
            atqa = null;
            if (statusData == null || statusData.Length < 3)
            {
                return false;
            }

            atqa = new byte[2];
            atqa[0] = statusData[0];
            atqa[1] = statusData[1];
            int uidLen = statusData[2];
            if (uidLen <= 0 || uidLen > 10 || statusData.Length < 3 + uidLen)
            {
                return false;
            }

            byte[] uid = new byte[uidLen];
            Buffer.BlockCopy(statusData, 3, uid, 0, uidLen);
            uidHex = ToHex(uid);
            return true;
        }

        /// <summary>
        /// If 0x5d response carries extra bytes after status, treat them as UID:
        /// length-prefixed (same as PiccCheck serialno) or raw 4/7/10-byte UID.
        /// </summary>
        public static string TryReadProvisionUid(byte[] statusData)
        {
            if (statusData == null || statusData.Length == 0)
            {
                return null;
            }

            string uid;
            byte[] atqa;
            if (TryReadPiccCheckUid(statusData, out uid, out atqa))
            {
                return uid;
            }

            if (statusData.Length == 4 || statusData.Length == 7 || statusData.Length == 10)
            {
                return ToHex(statusData);
            }

            if (statusData.Length >= 1)
            {
                int n = statusData[0];
                if (n > 0 && n <= 10 && statusData.Length >= 1 + n)
                {
                    byte[] uidBytes = new byte[n];
                    Buffer.BlockCopy(statusData, 1, uidBytes, 0, n);
                    return ToHex(uidBytes);
                }
            }

            return null;
        }

        public static string DescribeStatus(ushort status)
        {
            switch (status)
            {
                case 0:
                    return "成功";
                case 1:
                    return "无卡";
                default:
                    return "失败: 状态 0x" + status.ToString("X4");
            }
        }

        /// <summary>Build/parse round-trip used as a no-hardware sanity check.</summary>
        public static void SelfCheck()
        {
            byte[] payload = NdefText.BuildBytes("123456789", "A001");
            byte[] wire = BuildPicc(PiccOpcodes.NtagProvision, payload);
            FrameParseResult parsed = TryParse(wire, 0, wire.Length);
            if (!parsed.Ok)
            {
                throw new InvalidOperationException("协议自检失败: " + parsed.Error);
            }

            if (parsed.Frame.Module != PiccOpcodes.PiccModule ||
                parsed.Frame.SubCommand != PiccOpcodes.NtagProvision)
            {
                throw new InvalidOperationException("协议自检失败: 模块/子命令不匹配");
            }

            if (parsed.Frame.Payload.Length != payload.Length)
            {
                throw new InvalidOperationException("协议自检失败: 载荷长度不匹配");
            }

            for (int i = 0; i < payload.Length; i++)
            {
                if (parsed.Frame.Payload[i] != payload[i])
                {
                    throw new InvalidOperationException("协议自检失败: 载荷内容不匹配");
                }
            }

            // Simulated PiccCheck OK response with 7-byte NTAG UID.
            byte[] checkPayload = new byte[]
            {
                0x00, 0x00,
                0x44, 0x00,
                0x07,
                0x04, 0x54, 0xEA, 0xDA, 0x59, 0x1D, 0x80
            };
            byte[] checkWire = BuildPicc(PiccOpcodes.PiccCheckResp, checkPayload);
            FrameParseResult checkParsed = TryParse(checkWire, 0, checkWire.Length);
            string uid;
            byte[] atqa;
            if (!checkParsed.Ok || !checkParsed.Frame.IsSuccess ||
                !TryReadPiccCheckUid(checkParsed.Frame.StatusData, out uid, out atqa) ||
                uid != "0454EADA591D80")
            {
                throw new InvalidOperationException("协议自检失败: PiccCheck UID 解析");
            }
        }

        public static IList<string> KnownCommandsSummary()
        {
            return new List<string>
            {
                "STX 0x02 + module + subcmd + LEN_BE + payload + CRC16/XMODEM",
                "PICC module 0xBA (alt 0xC3)",
                "0x01 PiccOpen / 0x03 PiccClose / 0x05 PiccCheck(mode='A') → UID",
                "0x53 AntennaOn / 0x51 AntennaOff / 0x0B Halt",
                "0x5D provision NDEF text → resp 0x5E status[+optional UID]"
            };
        }
    }
}
