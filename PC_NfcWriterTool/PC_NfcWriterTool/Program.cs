using System;
using System.Threading;
using System.Windows.Forms;
using PC_NfcWriterTool.Forms;
using PC_NfcWriterTool.Protocol;

namespace PC_NfcWriterTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.ThreadException += OnUiException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                ReaderFrame.SelfCheck();
            }
            catch (Exception ex)
            {
                MessageBox.Show("协议自检失败，请检查 ReaderFrame 实现:\r\n" + ex.Message,
                    "UN655 NFC 发卡工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (var login = new LoginForm())
            {
                if (login.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                Application.Run(new MainForm(login.OperatorId));
            }
        }

        private static void OnUiException(object sender, ThreadExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.Message, "未处理异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            MessageBox.Show(ex == null ? e.ExceptionObject.ToString() : ex.Message,
                "未处理异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
