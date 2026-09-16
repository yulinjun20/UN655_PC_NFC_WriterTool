using System.Windows.Forms;

namespace PC_NfcWriterTool.Forms
{
    public static class StubDialogs
    {
        public static void NotInM1(string feature)
        {
            MessageBox.Show(
                feature + " 将在 M2/M3 实现。\r\n当前版本请使用「手动输入」加入队列后自动发卡。",
                "UN655 NFC 发卡工具",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
