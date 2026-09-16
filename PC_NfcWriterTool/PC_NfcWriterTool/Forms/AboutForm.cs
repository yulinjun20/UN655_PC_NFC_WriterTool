using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using PC_NfcWriterTool.Data;

namespace PC_NfcWriterTool.Forms
{
    public sealed class AboutForm : Form
    {
        public AboutForm()
        {
            Text = "关于";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(460, 260);
            BackColor = UiTheme.Bg;
            Font = UiTheme.Ui(10f);

            Controls.Add(new Label
            {
                Text = "UN655 NFC 工厂发卡工具",
                Font = UiTheme.Ui(14f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(30, 24)
            });

            Controls.Add(new Label
            {
                AutoSize = false,
                Size = new Size(400, 140),
                Location = new Point(30, 70),
                Text = "软件版本: 1.0.0-M1\r\n" +
                       "配套固件: MH2020C_NFC_Reader main ~70e20a7 / Phase 2.1\r\n" +
                       "读卡器须 NTAG_PROVISION_ENABLE=1\r\n" +
                       "通讯: 串口 115200，命令 0x5d 写入 NDEF 文本\r\n" +
                       "运行库: .NET Framework 4.7.2（Win7 需单独安装）\r\n" +
                       "主机入口: Program.cs"
            });

            var ok = UiTheme.PrimaryButton("确定");
            ok.Location = new Point(170, 210);
            ok.Size = new Size(100, 32);
            ok.Click += (s, e) => Close();
            Controls.Add(ok);
        }

        public static void OpenManual()
        {
            string path = AppPaths.ManualPath;
            if (!System.IO.File.Exists(path))
            {
                MessageBox.Show("未找到说明书: " + path, "帮助", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Process.Start(path);
        }
    }
}
