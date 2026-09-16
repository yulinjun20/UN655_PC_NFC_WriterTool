using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using PC_NfcWriterTool.Protocol;

namespace PC_NfcWriterTool.Serial
{
    public sealed class CardPresence
    {
        public bool Present { get; set; }
        public string UidHex { get; set; }
        public string Detail { get; set; }
        public ushort Status { get; set; }
    }

    public sealed class ProvisionResult
    {
        public bool Success { get; set; }
        public ushort Status { get; set; }
        public string UidHex { get; set; }
        public string Message { get; set; }
        public byte[] RawPayload { get; set; }
    }


    public sealed class ReadTagTextResult
    {
        public bool Success { get; set; }
        public ushort Status { get; set; }
        public string Text { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// MH2020C host UART client. 115200 8N1. Frames per pinpad/commandpro.c.
    /// </summary>
    public sealed class Mh2020cReader : IDisposable
    {
        private readonly object _sync = new object();
        private SerialPort _port;
        private readonly byte[] _rx = new byte[8192];
        private int _rxLen;

        public bool IsOpen
        {
            get
            {
                lock (_sync)
                {
                    return _port != null && _port.IsOpen;
                }
            }
        }

        public string PortName
        {
            get
            {
                lock (_sync)
                {
                    return _port == null ? null : _port.PortName;
                }
            }
        }

        public static string[] GetPortNames()
        {
            try
            {
                return SerialPort.GetPortNames();
            }
            catch
            {
                return new string[0];
            }
        }

        public void Open(string portName, int baudRate)
        {
            lock (_sync)
            {
                CloseUnlocked();
                _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    DtrEnable = true,
                    RtsEnable = true,
                    ReadTimeout = 200,
                    WriteTimeout = 2000,
                    Encoding = Encoding.ASCII
                };
                _port.Open();
                _rxLen = 0;
                try
                {
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                }
                catch
                {
                    // Some USB-serial adapters throw on discard; ignore.
                }
            }

            // Best-effort field bring-up; provision firmware may already have RF on.
            try
            {
                Exchange(PiccOpcodes.PiccOpen, new byte[0], PiccOpcodes.PiccOpenResp, 2000);
            }
            catch
            {
            }

            try
            {
                Exchange(PiccOpcodes.PiccAntennaOn, new byte[0], (byte)(PiccOpcodes.PiccAntennaOn + 1), 2000);
            }
            catch
            {
            }
        }

        public void Close()
        {
            lock (_sync)
            {
                if (IsOpen)
                {
                    try
                    {
                        ExchangeUnlocked(PiccOpcodes.PiccClose, new byte[0], PiccOpcodes.PiccCloseResp, 800);
                    }
                    catch
                    {
                    }
                }

                CloseUnlocked();
            }
        }

        public CardPresence CheckCard(int timeoutMs)
        {
            HostFrame frame;
            try
            {
                frame = Exchange(PiccOpcodes.PiccCheck, new byte[] { PiccOpcodes.CheckModeTypeA },
                    PiccOpcodes.PiccCheckResp, timeoutMs);
            }
            catch (TimeoutException)
            {
                return new CardPresence { Present = false, Detail = "通讯超时", Status = 0xFFFF };
            }
            catch (Exception ex)
            {
                return new CardPresence { Present = false, Detail = ex.Message, Status = 0xFFFE };
            }

            if (!frame.IsSuccess)
            {
                return new CardPresence
                {
                    Present = false,
                    Status = frame.StatusWord,
                    Detail = ReaderFrame.DescribeStatus(frame.StatusWord)
                };
            }

            string uid;
            byte[] atqa;
            ReaderFrame.TryReadPiccCheckUid(frame.StatusData, out uid, out atqa);
            return new CardPresence
            {
                Present = true,
                UidHex = uid,
                Status = 0,
                Detail = string.IsNullOrEmpty(uid) ? "已检测到卡" : ("SN " + uid)
            };
        }

        public ProvisionResult Provision(string materialNumber, string cardId, int timeoutMs)
        {
            byte[] payload = NdefText.BuildBytes(materialNumber, cardId);
            HostFrame frame;
            try
            {
                frame = Exchange(PiccOpcodes.NtagProvision, payload, PiccOpcodes.NtagProvisionResp, timeoutMs);
            }
            catch (TimeoutException)
            {
                return new ProvisionResult
                {
                    Success = false,
                    Status = 0xFFFF,
                    Message = "通讯超时"
                };
            }
            catch (Exception ex)
            {
                return new ProvisionResult
                {
                    Success = false,
                    Message = ex.Message
                };
            }

            var result = new ProvisionResult
            {
                Success = frame.IsSuccess,
                Status = frame.StatusWord,
                RawPayload = frame.Payload,
                UidHex = ReaderFrame.TryReadProvisionUid(frame.StatusData),
                Message = frame.IsSuccess ? "成功" : MapProvisionFail(frame.StatusWord)
            };
            return result;
        }


        public ReadTagTextResult ReadTagText(int timeoutMs)
        {
            HostFrame frame;
            try
            {
                frame = Exchange(PiccOpcodes.NtagReadText, new byte[0], PiccOpcodes.NtagReadTextResp, timeoutMs);
            }
            catch (TimeoutException)
            {
                return new ReadTagTextResult
                {
                    Success = false,
                    Status = 0xFFFF,
                    Message = "通讯超时"
                };
            }
            catch (Exception ex)
            {
                return new ReadTagTextResult
                {
                    Success = false,
                    Message = ex.Message
                };
            }

            if (!frame.IsSuccess)
            {
                return new ReadTagTextResult
                {
                    Success = false,
                    Status = frame.StatusWord,
                    Message = MapReadFail(frame.StatusWord)
                };
            }

            string textPayload = "";
            byte[] extra = frame.StatusData;
            if (extra != null && extra.Length > 0)
            {
                textPayload = Encoding.ASCII.GetString(extra).TrimEnd('\0');
            }

            return new ReadTagTextResult
            {
                Success = true,
                Status = 0,
                Text = textPayload,
                Message = "成功"
            };
        }

        public void HaltQuiet()
        {
            try
            {
                Exchange(PiccOpcodes.PiccHalt, new byte[0], (byte)(PiccOpcodes.PiccHalt + 1), 800);
            }
            catch
            {
            }
        }


        private static string MapReadFail(ushort status)
        {
            switch (status)
            {
                case 0:
                    return "成功";
                case 1:
                    return "失败: 无卡";
                case 2:
                    return "失败: 认证失败";
                default:
                    return "失败: 读TAG状态 0x" + status.ToString("X4");
            }
        }

        private static string MapProvisionFail(ushort status)
        {
            switch (status)
            {
                case 0:
                    return "成功";
                case 1:
                    return "失败: 无卡";
                case 2:
                    return "失败: 认证失败";
                case 3:
                    return "失败: 设密失败";
                default:
                    return "失败: 状态 0x" + status.ToString("X4");
            }
        }

        private HostFrame Exchange(byte subCommand, byte[] payload, byte expectedResp, int timeoutMs)
        {
            lock (_sync)
            {
                return ExchangeUnlocked(subCommand, payload, expectedResp, timeoutMs);
            }
        }

        private HostFrame ExchangeUnlocked(byte subCommand, byte[] payload, byte expectedResp, int timeoutMs)
        {
            if (_port == null || !_port.IsOpen)
            {
                throw new InvalidOperationException("串口未打开。");
            }

            _rxLen = 0;
            try
            {
                _port.DiscardInBuffer();
            }
            catch
            {
            }

            byte[] wire = ReaderFrame.BuildPicc(subCommand, payload);
            _port.Write(wire, 0, wire.Length);

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                PumpRead();
                FrameParseResult parsed = ReaderFrame.TryParse(_rx, 0, _rxLen);
                if (parsed.Ok && parsed.Frame != null)
                {
                    ShiftRx(parsed.Consumed);
                    if (parsed.Frame.Module != PiccOpcodes.PiccModule &&
                        parsed.Frame.Module != PiccOpcodes.PiccModuleAlt)
                    {
                        continue;
                    }

                    if (parsed.Frame.SubCommand != expectedResp &&
                        parsed.Frame.SubCommand != (byte)(subCommand + 1) &&
                        parsed.Frame.SubCommand != subCommand)
                    {
                        continue;
                    }

                    return parsed.Frame;
                }

                if (parsed.Consumed > 0)
                {
                    ShiftRx(parsed.Consumed);
                }

                Thread.Sleep(10);
            }

            throw new TimeoutException("等待读卡器应答超时。");
        }

        private void PumpRead()
        {
            try
            {
                int available = _port.BytesToRead;
                if (available <= 0)
                {
                    return;
                }

                int space = _rx.Length - _rxLen;
                if (space <= 0)
                {
                    _rxLen = 0;
                    space = _rx.Length;
                }

                int n = Math.Min(available, space);
                int got = _port.Read(_rx, _rxLen, n);
                _rxLen += got;
            }
            catch (TimeoutException)
            {
            }
        }

        private void ShiftRx(int consumed)
        {
            if (consumed <= 0)
            {
                return;
            }

            if (consumed >= _rxLen)
            {
                _rxLen = 0;
                return;
            }

            Buffer.BlockCopy(_rx, consumed, _rx, 0, _rxLen - consumed);
            _rxLen -= consumed;
        }

        private void CloseUnlocked()
        {
            if (_port != null)
            {
                try
                {
                    if (_port.IsOpen)
                    {
                        _port.Close();
                    }
                }
                catch
                {
                }

                _port.Dispose();
                _port = null;
            }

            _rxLen = 0;
        }

        public void Dispose()
        {
            Close();
        }
    }
}
