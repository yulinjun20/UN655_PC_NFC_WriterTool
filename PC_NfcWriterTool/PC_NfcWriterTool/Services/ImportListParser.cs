using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ExcelDataReader;
using PC_NfcWriterTool.Data;
using PC_NfcWriterTool.Protocol;

namespace PC_NfcWriterTool.Services
{
    public sealed class ImportRowResult
    {
        public int LineNumber { get; set; }
        public string MaterialNumber { get; set; }
        public string CardId { get; set; }
        public string Status { get; set; }
        public bool IsAcceptable { get; set; }
    }

    public sealed class ImportParseResult
    {
        public List<ImportRowResult> Rows { get; private set; }

        public ImportParseResult()
        {
            Rows = new List<ImportRowResult>();
        }

        public int Total { get { return Rows.Count; } }
        public int ValidCount { get; set; }
        public int FormatErrorCount { get; set; }
        public int DbDupCount { get; set; }
        public int FileDupCount { get; set; }
        public int QueueDupCount { get; set; }
    }

    /// <summary>
    /// Parse CSV/TXT/XLSX import lists. Header must be 物料号,ID号.
    /// </summary>
    public static class ImportListParser
    {
        public const string StatusPending = "待发";
        public const string StatusFormatError = "格式错误";
        public const string StatusDbDup = "库内重复";
        public const string StatusFileDup = "文件内重复";
        public const string StatusQueueDup = "队列已有";

        public static ImportParseResult Parse(
            string path,
            CardStore store,
            ISet<string> queueIds)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("未选择文件。", "path");
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("文件不存在。", path);
            }

            string ext = Path.GetExtension(path) ?? "";
            if (string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".xls", StringComparison.OrdinalIgnoreCase))
            {
                return ParseExcel(path, store, queueIds);
            }

            if (string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase))
            {
                return ParseDelimited(path, store, queueIds);
            }

            throw new InvalidOperationException("不支持的文件类型。请选择 .csv / .txt / .xlsx。");
        }

        private static ImportParseResult ParseDelimited(string path, CardStore store, ISet<string> queueIds)
        {
            string[] lines = File.ReadAllLines(path, DetectUtf8(path));
            if (lines.Length == 0)
            {
                throw new InvalidOperationException("文件为空，缺少表头「物料号,ID号」。");
            }

            int headerIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    headerIndex = i;
                    break;
                }
            }

            if (headerIndex < 0)
            {
                throw new InvalidOperationException("文件为空，缺少表头「物料号,ID号」。");
            }

            string[] headerParts = SplitCsvLine(lines[headerIndex]);
            if (!IsValidHeader(headerParts))
            {
                throw new InvalidOperationException(
                    "表头不正确。第一行必须是：物料号,ID号（逗号分隔）。");
            }

            var result = new ImportParseResult();
            var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int dataLine = 0;
            for (int i = headerIndex + 1; i < lines.Length; i++)
            {
                string raw = lines[i];
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                dataLine++;
                string[] parts = SplitCsvLine(raw);
                string mn = parts.Length > 0 ? (parts[0] ?? "").Trim() : "";
                string id = parts.Length > 1 ? (parts[1] ?? "").Trim() : "";
                AppendValidated(result, store, queueIds, seenInFile, dataLine, mn, id);
            }

            return result;
        }

        private static ImportParseResult ParseExcel(string path, CardStore store, ISet<string> queueIds)
        {
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                // First sheet only.
                if (!reader.Read())
                {
                    throw new InvalidOperationException("Excel 为空，缺少表头「物料号,ID号」。");
                }

                // Skip leading blank rows for header.
                string h0 = CellText(reader, 0);
                string h1 = CellText(reader, 1);
                while (string.IsNullOrWhiteSpace(h0) && string.IsNullOrWhiteSpace(h1))
                {
                    if (!reader.Read())
                    {
                        throw new InvalidOperationException("Excel 为空，缺少表头「物料号,ID号」。");
                    }

                    h0 = CellText(reader, 0);
                    h1 = CellText(reader, 1);
                }

                if (!IsValidHeader(new[] { h0, h1 }))
                {
                    throw new InvalidOperationException(
                        "表头不正确。第一行前两列必须是：物料号、ID号。");
                }

                var result = new ImportParseResult();
                var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int dataLine = 0;
                while (reader.Read())
                {
                    string mn = CellText(reader, 0);
                    string id = CellText(reader, 1);
                    if (string.IsNullOrWhiteSpace(mn) && string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    dataLine++;
                    AppendValidated(result, store, queueIds, seenInFile, dataLine,
                        (mn ?? "").Trim(), (id ?? "").Trim());
                }

                return result;
            }
        }

        private static void AppendValidated(
            ImportParseResult result,
            CardStore store,
            ISet<string> queueIds,
            HashSet<string> seenInFile,
            int lineNumber,
            string mn,
            string idRaw)
        {
            string id = (idRaw ?? "").ToUpperInvariant();
            var row = new ImportRowResult
            {
                LineNumber = lineNumber,
                MaterialNumber = mn,
                CardId = id
            };

            if (!NdefText.IsValidMaterialNumber(mn) || !NdefText.IsValidCardId(id))
            {
                row.Status = StatusFormatError;
                row.IsAcceptable = false;
                result.FormatErrorCount++;
            }
            else if (!seenInFile.Add(id))
            {
                row.Status = StatusFileDup;
                row.IsAcceptable = false;
                result.FileDupCount++;
            }
            else if (store != null && store.IsIdReserved(id))
            {
                row.Status = StatusDbDup;
                row.IsAcceptable = false;
                result.DbDupCount++;
            }
            else if (queueIds != null && queueIds.Contains(id))
            {
                row.Status = StatusQueueDup;
                row.IsAcceptable = false;
                result.QueueDupCount++;
            }
            else
            {
                row.Status = StatusPending;
                row.IsAcceptable = true;
                result.ValidCount++;
            }

            result.Rows.Add(row);
        }

        private static bool IsValidHeader(string[] parts)
        {
            if (parts == null || parts.Length < 2)
            {
                return false;
            }

            string a = (parts[0] ?? "").Trim().TrimStart('\uFEFF');
            string b = (parts[1] ?? "").Trim();
            return string.Equals(a, "物料号", StringComparison.Ordinal)
                && string.Equals(b, "ID号", StringComparison.Ordinal);
        }

        private static string CellText(IExcelDataReader reader, int index)
        {
            if (index < 0 || index >= reader.FieldCount)
            {
                return "";
            }

            object value = reader.GetValue(index);
            if (value == null || value == DBNull.Value)
            {
                return "";
            }

            return Convert.ToString(value) ?? "";
        }

        /// <summary>Split a simple CSV line; supports quoted fields.</summary>
        public static string[] SplitCsvLine(string line)
        {
            if (line == null)
            {
                return new string[0];
            }

            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        fields.Add(sb.ToString());
                        sb.Length = 0;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            fields.Add(sb.ToString());
            return fields.ToArray();
        }

        private static Encoding DetectUtf8(string path)
        {
            // File.ReadAllLines with UTF8 accepts BOM; Encoding.UTF8 strips BOM on read.
            return new UTF8Encoding(false, false);
        }
    }
}
