# UN655 NFC 工厂发卡工具（PC_NfcWriterTool）

单机 WinForms 上位机：通过串口向 MH2020C 读卡器发送 **0x5d** 发卡命令，把 `MN` + `ID` 写成 NDEF 文本；密钥派生与设密在读卡器固件（Phase 2.1，`NTAG_PROVISION_ENABLE=1`）内完成。PC 只做队列、防重、日志和工号。

当前交付：**M1**（登录、串口、0x5d 组帧、自动发卡循环、SQLite 防重）。导入 Excel / Inno 安装包为 M2/M3 占位。

## 打开工程

1. 安装 [Visual Studio 2017+](https://visualstudio.microsoft.com/) 或 Build Tools，勾选 **.NET 桌面开发**。
2. **Windows 7 必须先安装 [.NET Framework 4.7.2](https://dotnet.microsoft.com/download/dotnet-framework/net472)**，否则无法运行。
3. 双击仓库根目录 `PC_NfcWriterTool.sln`。
4. 配置选 Debug|Any CPU，生成。SQLite 互操作库已放在 `packages/`，一般无需再还原 NuGet。
5. 入口：`PC_NfcWriterTool/Program.cs` → 登录窗体 → `MainForm`。

命令行（开发机已装 VS）：

```bat
msbuild PC_NfcWriterTool.sln /p:Configuration=Debug /p:Platform="Any CPU"
PC_NfcWriterTool\bin\Debug\PC_NfcWriterTool.exe
```

## 每天开工（M1）

1. 启动后输入**操作员工号**（非空）。
2. 选择 COM 口，波特率固定 **115200 8N1**，点「打开」。状态变为「已连接」。
3. 工具 → 手动输入模式，密码 `12345678`，填写 9 位物料号 + 4 位 ID，「加入队列」。
4. 点「开始自动发卡」，连续放已格式化（CC=`E1`）的 NTAG213。
5. 成功后拿开卡再放下一张。同一 ID 写入本地库后不能再发（作废后也不释放）。

数据文件：程序目录 `data\nfc_writer.db`（工具菜单可打开数据目录）。

## 协议摘要

读卡器仓库 `https://github.com/yulinjun20/MH2020C_NFC_Reader`（main ~`70e20a7`）当前为私有。同作者公开工程 [UN637-OS_FreeRTOS](https://github.com/yulinjun20/UN637-OS_FreeRTOS) 的 `pinpad/commandpro.c` 使用同一套主机 UART 帧。完整字段见 `docs/protocol.md`。

- 帧：`STX(0x02)` + `module` + `subcmd` + `LEN`（大端，仅 payload）+ payload + `CRC16/XMODEM`（高字节在前）。STX 不参与 CRC。
- PICC 模块号 `0xBA`。查卡 `0x05`（mode=`'A'`）返回 UID；发卡 `0x5d` payload 为 ASCII：

```
MN:123456789\r\nID:A001\r\n
```

若 `0x5d` 应答不含 UID，工具会先发 `0x05` PiccCheck 再写卡，以便 SQLite 记录 SN。

## 模板

`templates/发卡导入模板.csv` 与 `.txt` 列名固定：`物料号,ID号`。Excel 导入为 M2。

## M2/M3 未做

- 文件 → 导入清单（Excel/TXT/CSV 预检）
- 导出 Excel、Inno Setup `Setup.exe`
- 量产读卡固件应关闭 provision；本工具只用于工厂发卡固件

需求变更请通过 UN655NFC需求助手 / 项目接口人确认后再改程序。
