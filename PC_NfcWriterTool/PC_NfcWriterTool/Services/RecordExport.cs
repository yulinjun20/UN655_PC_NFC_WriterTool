using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PC_NfcWriterTool.Data;

namespace PC_NfcWriterTool.Services
{
    /// <summary>
    /// Export issued-card query results as UTF-8 BOM CSV (not real xlsx).
    /// </summary>
    public static class RecordExport
    {
        public static void WriteIssuedCsv(IList<IssuedCard> rows, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("未指定导出路径。", "path");
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var utf8Bom = new UTF8Encoding(true);
            using (var writer = new StreamWriter(path, false, utf8Bom))
            {
                writer.WriteLine(string.Join(",", new[]
                {
                    Escape("SN"),
                    Escape("物料号"),
                    Escape("ID号"),
                    Escape("工号"),
                    Escape("发卡时间"),
                    Escape("状态")
                }));

                if (rows == null)
                {
                    return;
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    IssuedCard row = rows[i];
                    if (row == null)
                    {
                        continue;
                    }

                    string status = row.Status == IssueStatus.Voided ? "已作废" : "成功";
                    string time = row.IssuedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    writer.WriteLine(string.Join(",", new[]
                    {
                        Escape(row.SerialNumber ?? ""),
                        Escape(row.MaterialNumber ?? ""),
                        Escape(row.CardId ?? ""),
                        Escape(row.OperatorId ?? ""),
                        Escape(time),
                        Escape(status)
                    }));
                }
            }
        }

        public static string Escape(string value)
        {
            if (value == null)
            {
                value = "";
            }

            bool needQuote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (!needQuote)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static string DefaultFileName()
        {
            return "发卡记录_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv";
        }
    }
}
