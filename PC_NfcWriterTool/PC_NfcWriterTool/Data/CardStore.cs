using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PC_NfcWriterTool.Data
{
    /// <summary>
    /// Per-PC SQLite store. Successfully issued and voided IDs stay reserved forever.
    /// </summary>
    public sealed class CardStore : IDisposable
    {
        private readonly SQLiteConnection _connection;
        private readonly object _sync = new object();
        private readonly System.Collections.Generic.HashSet<string> _issuedSnCache =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);

        public CardStore(string dbPath)
        {
            string dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = dbPath,
                Version = 3,
                JournalMode = SQLiteJournalModeEnum.Wal,
                FailIfMissing = false,
                ForeignKeys = true
            };
            _connection = new SQLiteConnection(builder.ConnectionString);
            _connection.Open();
            InitSchema();
            ReloadIssuedSnCache();
        }

        public static CardStore OpenDefault()
        {
            return new CardStore(AppPaths.DatabaseFile);
        }

        private void InitSchema()
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS issued_ids (
    card_id      TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
    material_no  TEXT NOT NULL,
    sn           TEXT,
    operator_id  TEXT NOT NULL,
    issued_at    TEXT NOT NULL,
    status       TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS write_logs (
    log_id       INTEGER PRIMARY KEY AUTOINCREMENT,
    wrote_at     TEXT NOT NULL,
    sn           TEXT,
    material_no  TEXT NOT NULL,
    card_id      TEXT NOT NULL,
    operator_id  TEXT NOT NULL,
    result       TEXT NOT NULL,
    detail       TEXT
);

CREATE TABLE IF NOT EXISTS key_archive (
    id           INTEGER PRIMARY KEY CHECK (id = 1),
    file_path    TEXT NOT NULL,
    sha256       TEXT NOT NULL,
    file_size    INTEGER NOT NULL,
    loaded_at    TEXT NOT NULL
);
";
                cmd.ExecuteNonQuery();
            }
        }

        private void ReloadIssuedSnCache()
        {
            lock (_sync)
            {
                _issuedSnCache.Clear();
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT sn FROM issued_ids WHERE sn IS NOT NULL AND sn != ''";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string key = NormalizeSn(reader.IsDBNull(0) ? null : reader.GetString(0));
                            if (!string.IsNullOrEmpty(key))
                            {
                                _issuedSnCache.Add(key);
                            }
                        }
                    }
                }
            }
        }

        private void RememberIssuedSn(string sn)
        {
            string key = NormalizeSn(sn);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            lock (_sync)
            {
                _issuedSnCache.Add(key);
            }
        }

        private void ForgetIssuedSn(string sn)
        {
            string key = NormalizeSn(sn);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            lock (_sync)
            {
                _issuedSnCache.Remove(key);
            }
        }

        public bool IsIdReserved(string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                return false;
            }

            lock (_sync)
            {
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT 1 FROM issued_ids WHERE card_id = @id LIMIT 1";
                    cmd.Parameters.AddWithValue("@id", cardId.ToUpperInvariant());
                    object value = cmd.ExecuteScalar();
                    return value != null && value != DBNull.Value;
                }
            }
        }

        /// <summary>
        /// True if this physical tag UID/SN already has a successful (or voided) issue row.
        /// </summary>
        public bool IsSnIssued(string sn)
        {
            string key = NormalizeSn(sn);
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            lock (_sync)
            {
                if (_issuedSnCache.Contains(key))
                {
                    return true;
                }

                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = @"SELECT 1 FROM issued_ids
WHERE sn IS NOT NULL AND sn != ''
  AND REPLACE(REPLACE(UPPER(sn), ':', ''), '-', '') = @sn
LIMIT 1";
                    cmd.Parameters.AddWithValue("@sn", key);
                    object value = cmd.ExecuteScalar();
                    bool found = value != null && value != DBNull.Value;
                    if (found)
                    {
                        _issuedSnCache.Add(key);
                    }
                    return found;
                }
            }
        }

        /// <summary>
        /// Delete all issued_ids rows for this physical SN. Returns number of rows removed.
        /// write_logs kept; appends one delete audit line.
        /// </summary>
        public int DeleteIssuedBySn(string sn, string operatorId)
        {
            string key = NormalizeSn(sn);
            if (string.IsNullOrEmpty(key))
            {
                return 0;
            }

            lock (_sync)
            {
                using (var tx = _connection.BeginTransaction())
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"SELECT card_id, material_no, sn FROM issued_ids
WHERE sn IS NOT NULL AND sn != ''
  AND REPLACE(REPLACE(UPPER(sn), ':', ''), '-', '') = @sn";
                    cmd.Parameters.AddWithValue("@sn", key);
                    var rows = new System.Collections.Generic.List<string[]>();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            rows.Add(new string[]
                            {
                                reader.IsDBNull(0) ? "" : reader.GetString(0),
                                reader.IsDBNull(1) ? "" : reader.GetString(1),
                                reader.IsDBNull(2) ? "" : reader.GetString(2)
                            });
                        }
                    }

                    if (rows.Count == 0)
                    {
                        return 0;
                    }

                    cmd.Parameters.Clear();
                    cmd.CommandText = @"DELETE FROM issued_ids
WHERE sn IS NOT NULL AND sn != ''
  AND REPLACE(REPLACE(UPPER(sn), ':', ''), '-', '') = @sn";
                    cmd.Parameters.AddWithValue("@sn", key);
                    int n = cmd.ExecuteNonQuery();

                    for (int i = 0; i < rows.Count; i++)
                    {
                        InsertLog(cmd, DateTime.Now, rows[i][2], rows[i][1], rows[i][0],
                            operatorId, "Fail", "删除已发卡记录以便重新发卡");
                    }

                    tx.Commit();
                    ForgetIssuedSn(key);
                    return n;
                }
            }
        }

        public static string NormalizeSn(string sn)
        {
            if (string.IsNullOrEmpty(sn))
            {
                return null;
            }

            var sb = new StringBuilder(sn.Length);
            for (int i = 0; i < sn.Length; i++)
            {
                char c = sn[i];
                if (c == ' ' || c == ':' || c == '-' || c == '\t')
                {
                    continue;
                }

                sb.Append(char.ToUpperInvariant(c));
            }

            return sb.Length == 0 ? null : sb.ToString();
        }

        public IssuedCard GetIssued(string cardId)
        {
            lock (_sync)
            {
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = @"SELECT card_id, material_no, sn, operator_id, issued_at, status
FROM issued_ids WHERE card_id = @id";
                    cmd.Parameters.AddWithValue("@id", cardId.ToUpperInvariant());
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            return null;
                        }

                        return ReadIssued(reader);
                    }
                }
            }
        }

        public void RecordSuccess(string cardId, string materialNo, string sn, string operatorId, DateTime issuedAt)
        {
            string id = cardId.ToUpperInvariant();
            lock (_sync)
            {
                using (var tx = _connection.BeginTransaction())
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT INTO issued_ids (card_id, material_no, sn, operator_id, issued_at, status)
VALUES (@id, @mn, @sn, @op, @at, 'Success')";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@mn", materialNo);
                    string snNorm = NormalizeSn(sn);
                    cmd.Parameters.AddWithValue("@sn", (object)snNorm ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@op", operatorId);
                    cmd.Parameters.AddWithValue("@at", issuedAt.ToString("o", CultureInfo.InvariantCulture));
                    try
                    {
                        cmd.ExecuteNonQuery();
                    }
                    catch (SQLiteException ex)
                    {
                        if (ex.ResultCode == SQLiteErrorCode.Constraint)
                        {
                            throw new InvalidOperationException("ID 已在本地库中（含已作废），不能再发: " + id, ex);
                        }

                        throw;
                    }

                    InsertLog(cmd, issuedAt, sn, materialNo, id, operatorId, "Success", "成功");
                    tx.Commit();
                    RememberIssuedSn(snNorm);
                }
            }
        }

        public void RecordFailure(string cardId, string materialNo, string sn, string operatorId, DateTime at, string detail)
        {
            lock (_sync)
            {
                using (var cmd = _connection.CreateCommand())
                {
                    InsertLog(cmd, at, sn, materialNo, cardId == null ? "" : cardId.ToUpperInvariant(),
                        operatorId, "Fail", detail ?? "失败");
                }
            }
        }

        /// <summary>M2: voiding keeps the ID reserved so it can never be reused.</summary>
        public void VoidId(string cardId, string operatorId)
        {
            lock (_sync)
            {
                IssuedCard issued = GetIssued(cardId);
                if (issued == null)
                {
                    throw new InvalidOperationException("库中没有该 ID，无法作废。");
                }

                if (issued.Status == IssueStatus.Voided)
                {
                    throw new InvalidOperationException("该 ID 已作废，无需重复操作。");
                }

                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE issued_ids SET status = 'Voided' WHERE card_id = @id";
                    cmd.Parameters.AddWithValue("@id", cardId.ToUpperInvariant());
                    cmd.ExecuteNonQuery();
                }

                RecordFailure(cardId, issued.MaterialNumber, issued.SerialNumber, operatorId,
                    DateTime.Now, "已作废（ID 永不重用）");
            }
        }

        public IList<WriteLog> RecentLogs(int take)
        {
            var list = new List<WriteLog>();
            lock (_sync)
            {
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = @"SELECT log_id, wrote_at, sn, material_no, card_id, operator_id, result, detail
FROM write_logs ORDER BY log_id DESC LIMIT @n";
                    cmd.Parameters.AddWithValue("@n", take);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(ReadLog(reader));
                        }
                    }
                }
            }

            return list;
        }

        public IList<IssuedCard> QueryIssued(string sn, string mn, string id, string operatorId)
        {
            return QueryIssued(sn, mn, id, operatorId, null);
        }

        public IList<IssuedCard> QueryIssued(string sn, string mn, string id, string operatorId,
            IssueStatus? statusFilter)
        {
            var list = new List<IssuedCard>();
            var sql = new StringBuilder(@"SELECT card_id, material_no, sn, operator_id, issued_at, status FROM issued_ids WHERE 1=1");
            lock (_sync)
            {
                using (var cmd = _connection.CreateCommand())
                {
                    if (!string.IsNullOrWhiteSpace(sn))
                    {
                        sql.Append(" AND sn LIKE @sn");
                        cmd.Parameters.AddWithValue("@sn", "%" + sn.Trim() + "%");
                    }

                    if (!string.IsNullOrWhiteSpace(mn))
                    {
                        sql.Append(" AND material_no LIKE @mn");
                        cmd.Parameters.AddWithValue("@mn", "%" + mn.Trim() + "%");
                    }

                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        sql.Append(" AND card_id LIKE @id");
                        cmd.Parameters.AddWithValue("@id", "%" + id.Trim() + "%");
                    }

                    if (!string.IsNullOrWhiteSpace(operatorId))
                    {
                        sql.Append(" AND operator_id LIKE @op");
                        cmd.Parameters.AddWithValue("@op", "%" + operatorId.Trim() + "%");
                    }

                    if (statusFilter.HasValue)
                    {
                        sql.Append(statusFilter.Value == IssueStatus.Voided
                            ? " AND status = 'Voided'"
                            : " AND status = 'Success'");
                    }

                    sql.Append(" ORDER BY issued_at DESC LIMIT 500");
                    cmd.CommandText = sql.ToString();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(ReadIssued(reader));
                        }
                    }
                }
            }

            return list;
        }

        public KeyArchiveInfo GetKeyArchive()
        {
            lock (_sync)
            {
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT file_path, sha256, file_size, loaded_at FROM key_archive WHERE id = 1";
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            return null;
                        }

                        return new KeyArchiveInfo
                        {
                            FilePath = reader.GetString(0),
                            Sha256 = reader.GetString(1),
                            FileSize = reader.GetInt64(2),
                            LoadedAt = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                        };
                    }
                }
            }
        }

        public KeyArchiveInfo SaveKeyArchive(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("密钥文件不存在。", filePath);
            }

            byte[] bytes = File.ReadAllBytes(filePath);
            string sha;
            using (SHA256 sha256 = SHA256.Create())
            {
                sha = ReaderHex(sha256.ComputeHash(bytes));
            }

            var info = new KeyArchiveInfo
            {
                FilePath = Path.GetFullPath(filePath),
                Sha256 = sha,
                FileSize = bytes.Length,
                LoadedAt = DateTime.Now
            };

            lock (_sync)
            {
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = @"INSERT OR REPLACE INTO key_archive (id, file_path, sha256, file_size, loaded_at)
VALUES (1, @p, @s, @z, @t)";
                    cmd.Parameters.AddWithValue("@p", info.FilePath);
                    cmd.Parameters.AddWithValue("@s", info.Sha256);
                    cmd.Parameters.AddWithValue("@z", info.FileSize);
                    cmd.Parameters.AddWithValue("@t", info.LoadedAt.ToString("o", CultureInfo.InvariantCulture));
                    cmd.ExecuteNonQuery();
                }
            }

            return info;
        }

        public void ClearKeyArchive()
        {
            lock (_sync)
            {
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM key_archive WHERE id = 1";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static void InsertLog(SQLiteCommand cmd, DateTime at, string sn, string mn, string id, string op, string result, string detail)
        {
            cmd.Parameters.Clear();
            cmd.CommandText = @"INSERT INTO write_logs (wrote_at, sn, material_no, card_id, operator_id, result, detail)
VALUES (@at, @sn, @mn, @id, @op, @r, @d)";
            cmd.Parameters.AddWithValue("@at", at.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@sn", (object)sn ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mn", mn ?? "");
            cmd.Parameters.AddWithValue("@id", id ?? "");
            cmd.Parameters.AddWithValue("@op", op ?? "");
            cmd.Parameters.AddWithValue("@r", result);
            cmd.Parameters.AddWithValue("@d", (object)detail ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        private static IssuedCard ReadIssued(IDataRecord reader)
        {
            return new IssuedCard
            {
                CardId = reader.GetString(0),
                MaterialNumber = reader.GetString(1),
                SerialNumber = reader.IsDBNull(2) ? null : reader.GetString(2),
                OperatorId = reader.GetString(3),
                IssuedAt = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Status = string.Equals(reader.GetString(5), "Voided", StringComparison.OrdinalIgnoreCase)
                    ? IssueStatus.Voided
                    : IssueStatus.Success
            };
        }

        private static WriteLog ReadLog(IDataRecord reader)
        {
            return new WriteLog
            {
                LogId = reader.GetInt64(0),
                WroteAt = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                SerialNumber = reader.IsDBNull(2) ? null : reader.GetString(2),
                MaterialNumber = reader.GetString(3),
                CardId = reader.GetString(4),
                OperatorId = reader.GetString(5),
                Result = string.Equals(reader.GetString(6), "Success", StringComparison.OrdinalIgnoreCase)
                    ? WriteResultKind.Success
                    : WriteResultKind.Fail,
                Detail = reader.IsDBNull(7) ? null : reader.GetString(7)
            };
        }

        private static string ReaderHex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++)
            {
                sb.Append(data[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _connection.Dispose();
            }
        }
    }
}
