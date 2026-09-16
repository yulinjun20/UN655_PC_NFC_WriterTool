namespace PC_NfcWriterTool.Protocol
{
    /// <summary>
    /// Host UART opcodes from pinpad/commandpro.h + Picc_Pro() in commandpro.c
    /// (yulinjun20/UN637-OS_FreeRTOS, same family as MH2020C_NFC_Reader).
    /// </summary>
    public static class PiccOpcodes
    {
        /// <summary>STX is sent on the wire but is NOT part of cmdBuf / CRC.</summary>
        public const byte Stx = 0x02;

        /// <summary>PICC_CODE in commandpro.h (primary PICC module id).</summary>
        public const byte PiccModule = 0xBA;

        /// <summary>PICC_CODE2 alternate module id (also dispatched to Picc_Pro).</summary>
        public const byte PiccModuleAlt = 0xC3;

        public const byte PiccOpen = 0x01;
        public const byte PiccOpenResp = 0x02;
        public const byte PiccClose = 0x03;
        public const byte PiccCloseResp = 0x04;

        /// <summary>
        /// Dll_PiccCheck — returns card type + length-prefixed UID (serialno).
        /// Request payload: 1 byte mode ('A'=0x41 Type A / NTAG213).
        /// </summary>
        public const byte PiccCheck = 0x05;
        public const byte PiccCheckResp = 0x06;

        public const byte PiccHalt = 0x0B;
        public const byte PiccReset = 0x0D;
        public const byte PiccAntennaOff = 0x51;
        public const byte PiccAntennaOn = 0x53;
        public const byte PiccAntennaOnAlt = 0x55;

        /// <summary>
        /// Factory provision command (Phase 2.1, NTAG_PROVISION_ENABLE=1).
        /// Payload = NDEF text consumed by Dll_NfcWriteTextToTag:
        /// MN:123456789\r\nID:A001\r\n
        /// Response sub-command is request+1 (0x5E), matching Picc_Pro cmdBuf[1]++.
        /// Public UN637 commandpro.c does not yet contain case 0x5d; MH2020C_NFC_Reader
        /// @ 70e20a7 is the provision firmware. Frame layout is identical to other PICC cmds.
        /// </summary>
        public const byte NtagProvision = 0x5D;
        public const byte NtagProvisionResp = 0x5E;

        /// <summary>
        /// Read NDEF text from tag (Dll_NfcReadTextFromTag).
        /// Response sub-command is request+1 (0x5B). Success payload = status(2) + ASCII text.
        /// </summary>
        public const byte NtagReadText = 0x5A;
        public const byte NtagReadTextResp = 0x5B;

        /// <summary>ISO14443 Type A / NTAG check mode for Dll_PiccCheck.</summary>
        public const byte CheckModeTypeA = (byte)'A';
    }
}
