using System;
using System.Threading;
using PC_NfcWriterTool.Data;
using PC_NfcWriterTool.Protocol;
using PC_NfcWriterTool.Serial;

namespace PC_NfcWriterTool.Services
{
    public sealed class AutoWriteProgress
    {
        public string BigStatus { get; set; }
        public bool SuccessLook { get; set; }
        public bool FailLook { get; set; }
        public QueueItem Current { get; set; }
        public WriteAttempt LastAttempt { get; set; }
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
        public int QueueRemaining { get; set; }
        public int QueueIndex { get; set; }
        public int QueueTotal { get; set; }
        public bool Running { get; set; }
        public bool WaitingForRemoval { get; set; }
    }

    /// <summary>
    /// Factory loop: wait for card → 0x5d provision → 0x5a read-back compare → SQLite → next item.
    /// After success, wait until the tag leaves the field so the next ID is not
    /// written to the same card.
    /// </summary>
    public sealed class AutoWriteEngine : IDisposable
    {
        private readonly PendingQueue _queue;
        private readonly CardStore _store;
        private readonly Mh2020cReader _reader;
        private readonly string _operatorId;
        private readonly SynchronizationContext _ui;
        private Thread _thread;
        private volatile bool _run;
        private volatile bool _pauseRequested;
        private int _success;
        private int _fail;
        private int _sessionTotal;

        public event Action<AutoWriteProgress> Progress;

        private string _exitStatus = "已暂停";

        public AutoWriteEngine(PendingQueue queue, CardStore store, Mh2020cReader reader, string operatorId)
        {
            _queue = queue;
            _store = store;
            _reader = reader;
            _operatorId = operatorId;
            _ui = SynchronizationContext.Current;
            _sessionTotal = Math.Max(queue.Count, queue.OriginalTotal);
        }

        public int SuccessCount { get { return _success; } }
        public int FailCount { get { return _fail; } }
        public bool IsRunning { get { return _run; } }

        public void Start()
        {
            if (_run)
            {
                return;
            }

            _pauseRequested = false;
            _exitStatus = "已暂停";
            _run = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "NfcAutoWrite" };
            _thread.Start();
        }

        public void Pause()
        {
            _pauseRequested = true;
            _run = false;
        }

        public void Skip()
        {
            _queue.SkipCurrentKeepInQueue();
            Raise(new AutoWriteProgress
            {
                BigStatus = "已跳过本条（仍留在队列末尾）",
                Current = _queue.Peek(),
                QueueRemaining = _queue.Count,
                Running = _run,
                SuccessCount = _success,
                FailCount = _fail,
                QueueTotal = Math.Max(_sessionTotal, _queue.Count)
            });
        }

        private void Loop()
        {
            RaiseWaiting("请放卡 / 等待中…");
            while (_run && !_pauseRequested)
            {
                QueueItem current = _queue.Peek();
                if (current == null)
                {
                    Raise(new AutoWriteProgress
                    {
                        BigStatus = "队列为空",
                        Running = true,
                        SuccessCount = _success,
                        FailCount = _fail,
                        QueueRemaining = 0
                    });
                    Sleep(400);
                    continue;
                }

                if (!_reader.IsOpen)
                {
                    RaiseFail(current, null, "串口未连接", false);
                    Sleep(500);
                    continue;
                }

                if (_store.IsIdReserved(current.CardId))
                {
                    Interlocked.Increment(ref _fail);
                    var attempt = FailAttempt(current, null, "失败: ID已在库中");
                    _store.RecordFailure(current.CardId, current.MaterialNumber, null, _operatorId, attempt.Time, attempt.Message);
                    Raise(new AutoWriteProgress
                    {
                        BigStatus = "失败: ID已在库中",
                        FailLook = true,
                        Current = current,
                        LastAttempt = attempt,
                        SuccessCount = _success,
                        FailCount = _fail,
                        QueueRemaining = _queue.Count,
                        Running = false
                    });
                    _exitStatus = "失败: ID已在库中";
                    Pause();
                    continue;
                }

                CardPresence card;
                try
                {
                    card = _reader.CheckCard(600);
                }
                catch (Exception ex)
                {
                    RaiseWaiting("读卡异常: " + ex.Message);
                    Sleep(300);
                    continue;
                }

                if (!card.Present)
                {
                    RaiseWaiting("请放卡 / 等待中…");
                    Sleep(180);
                    continue;
                }

                if (string.IsNullOrEmpty(card.UidHex))
                {
                    RaiseWaiting("已检测到卡但未读到 SN，请拿开重放");
                    Sleep(250);
                    continue;
                }

                if (_store.IsSnIssued(card.UidHex))
                {
                    Interlocked.Increment(ref _fail);
                    string snBlock = card.UidHex;
                    string msg = "失败: 该卡已发卡";
                    var blocked = FailAttempt(current, snBlock, msg);
                    _store.RecordFailure(current.CardId, current.MaterialNumber, snBlock, _operatorId, blocked.Time, msg);
                    Raise(new AutoWriteProgress
                    {
                        BigStatus = msg + "（重新发卡请用 工具→重新发卡）",
                        FailLook = true,
                        Current = current,
                        LastAttempt = blocked,
                        SuccessCount = _success,
                        FailCount = _fail,
                        QueueRemaining = _queue.Count,
                        Running = true
                    });
                    WaitCardRemoved();
                    continue;
                }

                Raise(new AutoWriteProgress
                {
                    BigStatus = "正在发卡…",
                    Current = current,
                    Running = true,
                    SuccessCount = _success,
                    FailCount = _fail,
                    QueueRemaining = _queue.Count
                });

                ProvisionResult provision = _reader.Provision(current.MaterialNumber, current.CardId, 8000);
                string sn = !string.IsNullOrEmpty(provision.UidHex) ? provision.UidHex : card.UidHex;
                DateTime now = DateTime.Now;

                if (provision.Success)
                {
                    ReadTagTextResult readBack = _reader.ReadTagText(5000);
                    if (!readBack.Success || !NdefText.Matches(current.MaterialNumber, current.CardId, readBack.Text))
                    {
                        string detail;
                        if (!readBack.Success)
                        {
                            detail = readBack.Message ?? "读回失败";
                        }
                        else
                        {
                            string gotMn;
                            string gotId;
                            if (NdefText.TryParse(readBack.Text, out gotMn, out gotId))
                            {
                                detail = "期望 MN:" + current.MaterialNumber + " ID:" + current.CardId
                                    + " 实得 MN:" + gotMn + " ID:" + gotId;
                            }
                            else
                            {
                                string raw = readBack.Text ?? "";
                                if (raw.Length > 40)
                                {
                                    raw = raw.Substring(0, 40) + "...";
                                }
                                detail = "无法解析读回文本: " + raw;
                            }
                        }

                        string failMsg = "失败: 读回比对失败 (" + detail + ")";
                        Interlocked.Increment(ref _fail);
                        var mismatch = FailAttempt(current, sn, failMsg);
                        _store.RecordFailure(current.CardId, current.MaterialNumber, sn, _operatorId, now, failMsg);
                        Raise(new AutoWriteProgress
                        {
                            BigStatus = failMsg,
                            FailLook = true,
                            Current = current,
                            LastAttempt = mismatch,
                            SuccessCount = _success,
                            FailCount = _fail,
                            QueueRemaining = _queue.Count,
                            Running = true
                        });
                        WaitCardRemoved();
                        continue;
                    }

                    try
                    {
                        _store.RecordSuccess(current.CardId, current.MaterialNumber, sn, _operatorId, now);
                    }
                    catch (InvalidOperationException dup)
                    {
                        Interlocked.Increment(ref _fail);
                        var dupAttempt = FailAttempt(current, sn, dup.Message);
                        _store.RecordFailure(current.CardId, current.MaterialNumber, sn, _operatorId, now, dup.Message);
                        Raise(new AutoWriteProgress
                        {
                            BigStatus = dup.Message,
                            FailLook = true,
                            Current = current,
                            LastAttempt = dupAttempt,
                            SuccessCount = _success,
                            FailCount = _fail,
                            QueueRemaining = _queue.Count,
                            Running = true
                        });
                        WaitCardRemoved();
                        continue;
                    }

                    Interlocked.Increment(ref _success);
                    _queue.Dequeue();
                    var ok = new WriteAttempt
                    {
                        Item = current,
                        SerialNumber = sn,
                        Success = true,
                        Message = "成功",
                        Time = now
                    };
                    Raise(new AutoWriteProgress
                    {
                        BigStatus = "成功",
                        SuccessLook = true,
                        Current = _queue.Peek(),
                        LastAttempt = ok,
                        SuccessCount = _success,
                        FailCount = _fail,
                        QueueRemaining = _queue.Count,
                        Running = true
                    });
                    WaitCardRemoved();
                }
                else
                {
                    Interlocked.Increment(ref _fail);
                    var attempt = FailAttempt(current, sn, provision.Message);
                    _store.RecordFailure(current.CardId, current.MaterialNumber, sn, _operatorId, now, provision.Message);
                    Raise(new AutoWriteProgress
                    {
                        BigStatus = provision.Message,
                        FailLook = true,
                        Current = current,
                        LastAttempt = attempt,
                        SuccessCount = _success,
                        FailCount = _fail,
                        QueueRemaining = _queue.Count,
                        Running = true
                    });
                    Sleep(400);
                }
            }

            _run = false;
            Raise(new AutoWriteProgress
            {
                BigStatus = _exitStatus,
                FailLook = _exitStatus.StartsWith("失败"),
                Current = _queue.Peek(),
                SuccessCount = _success,
                FailCount = _fail,
                QueueRemaining = _queue.Count,
                Running = false
            });
        }

        private void WaitCardRemoved()
        {
            Raise(new AutoWriteProgress
            {
                BigStatus = "请拿开卡",
                SuccessLook = true,
                WaitingForRemoval = true,
                Current = _queue.Peek(),
                SuccessCount = _success,
                FailCount = _fail,
                QueueRemaining = _queue.Count,
                Running = true
            });

            DateTime start = DateTime.UtcNow;
            while (_run && !_pauseRequested && DateTime.UtcNow - start < TimeSpan.FromSeconds(30))
            {
                CardPresence card = _reader.CheckCard(400);
                if (!card.Present)
                {
                    _reader.HaltQuiet();
                    return;
                }

                Sleep(200);
            }
        }

        private static WriteAttempt FailAttempt(QueueItem item, string sn, string message)
        {
            return new WriteAttempt
            {
                Item = item,
                SerialNumber = sn,
                Success = false,
                Message = message,
                Time = DateTime.Now
            };
        }

        private void RaiseWaiting(string text)
        {
            Raise(new AutoWriteProgress
            {
                BigStatus = text,
                Current = _queue.Peek(),
                SuccessCount = _success,
                FailCount = _fail,
                QueueRemaining = _queue.Count,
                QueueTotal = Math.Max(_sessionTotal, _queue.Count),
                Running = true
            });
        }

        private void RaiseFail(QueueItem current, string sn, string message, bool count)
        {
            if (count)
            {
                Interlocked.Increment(ref _fail);
            }

            Raise(new AutoWriteProgress
            {
                BigStatus = message,
                FailLook = true,
                Current = current,
                LastAttempt = current == null ? null : FailAttempt(current, sn, message),
                SuccessCount = _success,
                FailCount = _fail,
                QueueRemaining = _queue.Count,
                Running = true
            });
        }

        private void Raise(AutoWriteProgress progress)
        {
            progress.QueueIndex = progress.QueueRemaining == 0 ? 0 : 1;
            progress.QueueTotal = Math.Max(_sessionTotal, _queue.Count + _success);
            Action<AutoWriteProgress> handler = Progress;
            if (handler == null)
            {
                return;
            }

            if (_ui != null)
            {
                _ui.Post(state => handler((AutoWriteProgress)state), progress);
            }
            else
            {
                handler(progress);
            }
        }

        private static void Sleep(int ms)
        {
            Thread.Sleep(ms);
        }

        public void Dispose()
        {
            Pause();
            Thread t = _thread;
            if (t != null && t.IsAlive)
            {
                t.Join(1500);
            }
        }
    }
}
