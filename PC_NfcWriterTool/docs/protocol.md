# MH2020C 主机 UART 协议（M1 实测依据）

配套读卡器：`MH2020C_NFC_Reader` main ~`70e20a7`，须 `NTAG_PROVISION_ENABLE=1`。  
该 GitHub 仓库当前 **404/私有**，无法直接检出 `70e20a7`。同作者公开固件 [yulinjun20/UN637-OS_FreeRTOS](https://github.com/yulinjun20/UN637-OS_FreeRTOS) 含 **同一份** `pinpad/commandpro.c` / `commandpro.h`（Landi 风格 `Dll_Picc*` 封装）。下文帧格式以该文件为准。`0x5d` 在公开 UN637 源码中尚未出现 case，按 `Picc_Pro()` 现有「奇数请求 / 请求+1 应答 / 两字节状态」规律实现，供 Yu-PC 实卡联调。

## 物理层

| 项 | 值 |
|----|----|
| 波特率 | 115200（`PCI_COM_BAUD` in `User/user.h`） |
| 数据位 | 8N1 |
| 流控 | 无 |

## 帧格式（SendingCommand / WaitingCommand）

UART 字节序：

```
STX (0x02)
cmdBuf[0]   module
cmdBuf[1]   sub-command
cmdBuf[2]   LEN high
cmdBuf[3]   LEN low          LEN = payload 字节数（不含 module/sub/LEN/CRC）
cmdBuf[4 .. 4+LEN) payload
CRC16 high
CRC16 low
```

要点（对照 `SendingCommand`）：

- 先 `PortSend(0x02)`，再发送 `cmdBuf[0 .. cmdLen+4+2)`。
- `cmdLen = (cmdBuf[2]<<8) + cmdBuf[3]`。
- `Crc16CCITT(cmdBuf, cmdLen+4, crc)`，CRC **覆盖 cmdBuf 头 4 字节 + payload**，**不含 STX**。
- **无 ETX**；`WaitingCommand` 也不等待 ACK `0x06`。
- 接收状态机：`RECV_IDLE` 等到 `0x02` 后进入 `RECV_CMD` → `SUBCMD` → `LEN1` → `LEN2` → `DATA` → `CRC1` → `CRC2`。

CRC 实现：公开树中无 `Crc16CCITT` 源码（在闭源 lib）。本工具使用该 SDK 族常见定义 **CRC-16/XMODEM**（poly `0x1021`，init `0x0000`，非反射，xorout `0`，高字节在前）。解析时若 XMODEM 失败会再试 CCITT-FALSE（init `0xFFFF`）。Yu-PC 若 CRC 全失败，优先核对此算法。

模块号（`commandpro.h`）：

| 宏 | 值 | 处理函数 |
|----|----|----------|
| `PICC_CODE` | `0xBA` | `Picc_Pro()` |
| `PICC_CODE2` | `0xC3` | 同上 |
| `SYS_CODE` | `0xD1` | `System_Pro()` |

应答：`Picc_Pro` 普遍执行 `cmdBuf[1]++` 或写成请求+1；payload 前两字节为 `HI_BYTE(ABS(iRet))`、`LOW_BYTE(ABS(iRet))`，`0x0000` 成功。

## PICC 子命令（与发卡相关）

| 请求 | 应答 | Dll | 说明 |
|------|------|-----|------|
| `0x01` | `0x02` | `Dll_PiccOpen` | 打开场 |
| `0x03` | `0x04` | `Dll_PiccClose` | 关闭 |
| `0x05` | `0x06` | `Dll_PiccCheck(mode, cardtype, serialno)` | **读 UID/SN** |
| `0x0B` | `0x0C` | `Dll_PiccHaltA` | Halt |
| `0x51` | `0x52` | `Dll_PiccAntennaOff` | 关天线 |
| `0x53` / `0x55` | +1 | `Dll_PiccAntennaOn` | 开天线 |
| **`0x5D`** | **`0x5E`** | **`Dll_NfcWriteTextToTag`** | **Phase 2.1 发卡** |
| **`0x5A`** | **`0x5B`** | **`Dll_NfcReadTextFromTag`** | **读回 NDEF 文本（PWD_AUTH）** |

### 0x05 PiccCheck（本工具取 SN 用）

请求 payload：1 字节 `mode`。NTAG213 使用 `'A'`（`0x41`）。

成功应答 payload（`!iRet` 时 `LEN = 2 + serialno[0] + 1 + 2`）：

```
statusH statusL | cardtype[2] | serialno[0]=UID长度 | serialno[1..len]=UID
```

失败：仅 2 字节状态。

公开 `Picc_Pro` **没有把 UID 放进除 0x05/0x31 以外的应答**。因此即使 0x5d 成功，本工具仍 **先 0x05 再 0x5d**；若 0x5d 多带了 UID 则优先用应答里的 SN。

### 0x5D 发卡（Phase 2.1）

请求 payload = 与读卡器 `Dll_NfcWriteTextToTag` 兼容的 **NDEF 文本**（PC 不组 TLV，固件组 Text record）。

本工具组帧示例（`MN:123456789` + `ID:A001`，CRC-16/XMODEM）：

```
02 BA 5D 00 17 4D 4E 3A 31 32 33 34 35 36 37 38 39 0D 0A
49 44 3A 41 30 30 31 0D 0A 1B F7
```

`00 17` = payload 23 字节；末尾 `1B F7` 为 CRC。

```
MN:123456789\r\nID:A001\r\n
```

也接受 `\n`，发送前规范成 `\r\n`。物料号 9 位数字，ID 4 位字母或数字。

预期应答：

```
module=0xBA  sub=0x5E
payload: statusH statusL [optional UID]
```

状态 0 = 写 NDEF + 固件内派生密钥/设密成功。非 0 时界面显示 `失败: 状态 0xXXXX`（`0x0002` 映射为「认证失败」，`0x0003` 为「设密失败」，实卡码表以固件为准）。


### 0x5A 读回文本

请求 payload：空。固件内部对受保护区做 PWD_AUTH 后读 NDEF 文本。

成功应答：

```
module=0xBA  sub=0x5B
payload: statusH statusL | ASCII text (MN:...\r\nID:...\r\n, 最长约 200)
```

失败：仅 2 字节状态。自动发卡在 `0x5D` 成功后必须 `0x5A` 读回，并与当前队列 MN/ID 比对一致才记成功。

## 本工具发卡时序

1. 打开 COM → 尝试 `0x01` PiccOpen、`0x53` AntennaOn（失败不阻断，兼容已上电固件）。
2. 自动循环：`0x05` 轮询，无卡则大字「请放卡」。
3. 有卡：读 SN → 检查 SQLite ID 未占用 → 发 `0x5D` → 发 `0x5A` 读回比对。
4. 仅当写+读回均成功且 MN/ID 一致：记 SN/MN/ID/工号/时间，ID 永不重用；等待 `0x05` 失败（拿开卡）再发下一条。
5. 失败：本条留在队列头，可重试或「跳过本条」（默认移到队尾，不记成功）。

## 与固件职责

| 谁 | 做什么 |
|----|--------|
| PC | 队列、防重、日志、工号、组 `0x5d`+NDEF 文本 |
| 读卡器 | 读 SN、内置 Kf/D 派生 PWD/PACK、写保护 |
| PC 密钥 txt | 仅备案 SHA256，不参与运算 |
