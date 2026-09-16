using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace PC_NfcWriterTool.Data
{
    public static class AppPaths
    {
        public static string BaseDirectory
        {
            get
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                return string.IsNullOrEmpty(dir) ? Environment.CurrentDirectory : dir;
            }
        }

        /// <summary>
        /// Per-user SQLite under LocalAppData so Program Files installs can write
        /// without elevation, and uninstall can keep issued-card history.
        /// </summary>
        public static string DataDirectory
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "UN655",
                    "PC_NfcWriterTool",
                    "data");
                Directory.CreateDirectory(dir);

                // One-time migrate from old beside-exe data\ if present and target empty.
                string legacy = Path.Combine(BaseDirectory, "data", "nfc_writer.db");
                string target = Path.Combine(dir, "nfc_writer.db");
                if (!File.Exists(target) && File.Exists(legacy))
                {
                    try
                    {
                        File.Copy(legacy, target, false);
                    }
                    catch
                    {
                        // Keep going with empty/new DB; user can copy manually.
                    }
                }

                return dir;
            }
        }

        public static string DatabaseFile
        {
            get { return Path.Combine(DataDirectory, "nfc_writer.db"); }
        }

        public static string FindRepoFile(params string[] relativeParts)
        {
            string rel = Path.Combine(relativeParts);
            string[] roots =
            {
                BaseDirectory,
                Path.GetFullPath(Path.Combine(BaseDirectory, "..", "..", "..")),
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            };

            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root))
                {
                    continue;
                }

                string candidate = Path.GetFullPath(Path.Combine(root, rel));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.GetFullPath(Path.Combine(BaseDirectory, rel));
        }

        public static string ManualPath
        {
            get { return FindRepoFile("docs", "manual.md"); }
        }

        public static string ProtocolDocPath
        {
            get { return FindRepoFile("docs", "protocol.md"); }
        }

        public static void OpenDirectory(string path)
        {
            Directory.CreateDirectory(path);
            try
            {
                System.Diagnostics.Process.Start(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开目录: " + ex.Message, "UN655 NFC 发卡工具",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
