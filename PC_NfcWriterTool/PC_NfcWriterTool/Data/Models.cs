using System;

namespace PC_NfcWriterTool.Data
{
    public enum IssueStatus
    {
        Success = 0,
        Voided = 1
    }

    public enum WriteResultKind
    {
        Success = 0,
        Fail = 1
    }

    public sealed class QueueItem
    {
        public string MaterialNumber { get; set; }
        public string CardId { get; set; }
        public string Source { get; set; }
    }

    public sealed class IssuedCard
    {
        public string CardId { get; set; }
        public string MaterialNumber { get; set; }
        public string SerialNumber { get; set; }
        public string OperatorId { get; set; }
        public DateTime IssuedAt { get; set; }
        public IssueStatus Status { get; set; }
    }

    public sealed class WriteLog
    {
        public long LogId { get; set; }
        public DateTime WroteAt { get; set; }
        public string SerialNumber { get; set; }
        public string MaterialNumber { get; set; }
        public string CardId { get; set; }
        public string OperatorId { get; set; }
        public WriteResultKind Result { get; set; }
        public string Detail { get; set; }
    }

    public sealed class KeyArchiveInfo
    {
        public string FilePath { get; set; }
        public string Sha256 { get; set; }
        public DateTime LoadedAt { get; set; }
        public long FileSize { get; set; }
    }

    public sealed class WriteAttempt
    {
        public QueueItem Item { get; set; }
        public string SerialNumber { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public DateTime Time { get; set; }
    }
}
