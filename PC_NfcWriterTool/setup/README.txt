Inno Setup 安装包（M2）

前置：
1. Visual Studio 2022（.NET 桌面开发）
2. Inno Setup 6（ISCC.exe）

生成：
  powershell -ExecutionPolicy Bypass -File setup\build_setup.ps1

输出：
  setup\output\UN655_NFC_发卡工具_Setup_1.0.0.exe

安装内容：
- Release 程序与 SQLite / ExcelDataReader / x86+x64 Interop
- docs\、templates\
- 不打包 data\nfc_writer.db

用户数据：
  %LocalAppData%\UN655\PC_NfcWriterTool\data\nfc_writer.db
  卸载安装包时保留该目录（发卡/作废记录不丢）。

.NET：
  安装向导检测 4.7.2；缺失时提示打开微软下载页。
