Inno Setup 安装包（M3）

TODO: 使用 Inno Setup 6 生成 Setup.exe。

规划行为（见 docs/manual.md §2）：
- 安装 UN655 NFC 发卡工具到 Program Files
- 检测/提示安装 .NET Framework 4.7.2（Win7 刚需）
- 桌面快捷方式「UN655 NFC 发卡工具」
- 复制 templates\ 与 docs\

M1 不提供安装包。Yu-PC 请用 Visual Studio 打开 PC_NfcWriterTool.sln 生成后运行 bin\Debug\PC_NfcWriterTool.exe。
