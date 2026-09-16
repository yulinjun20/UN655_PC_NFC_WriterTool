using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PC_NfcWriterTool.Data;
using PC_NfcWriterTool.Protocol;
using PC_NfcWriterTool.Serial;
using PC_NfcWriterTool.Services;

namespace PC_NfcWriterTool.Forms
{
    public sealed class MainForm : Form
    {
        private readonly string _operatorId;
        private readonly CardStore _store;
        private readonly Mh2020cReader _reader = new Mh2020cReader();
        private readonly PendingQueue _queue = new PendingQueue();

        private ComboBox _ports;
        private Label _connDot;
        private Label _connText;
        private Label _operatorLabel;
        private Label _statsLabel;
        private Label _mnValue;
        private Label _idValue;
        private Label _progressLabel;
        private Label _bigStatus;
        private Panel _statusBox;
        private Button _btnStart;
        private Button _btnPause;
        private Button _btnSkip;
        private Button _btnReadTag;
        private ListView _recent;
        private ToolStripStatusLabel _keyStatus;
        private ToolStripStatusLabel _manualStatus;
        private AutoWriteEngine _engine;
        private bool _manualUnlocked;
        private ToolStripMenuItem _reissueMenuItem;
        private int _shiftOk;
        private int _shiftFail;

        public MainForm(string operatorId)
        {
            _operatorId = operatorId;
            _store = CardStore.OpenDefault();
            Text = "UN655 NFC 工厂发卡工具";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 620);
            Size = new Size(980, 680);
            BackColor = UiTheme.Bg;
            Font = UiTheme.Ui(9.5f);
            KeyPreview = true;

            BuildMenu();
            BuildLayout();
            RefreshPorts();
            RefreshQueuePanel();
            RefreshKeyStatus();
            LoadRecentFromDb();

            _queue.Changed += (s, e) =>
            {
                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(RefreshQueuePanel));
                }
            };

            FormClosing += OnClosing;
        }

        private void BuildMenu()
        {
            var menu = new MenuStrip();
            var file = new ToolStripMenuItem("文件(&F)");
            file.DropDownItems.Add("导入清单", null, (s, e) => OnImportList());
            file.DropDownItems.Add("导出记录", null, (s, e) => OnExportRecords());
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("退出", null, (s, e) => Close());

            var tools = new ToolStripMenuItem("工具(&T)");
            tools.DropDownItems.Add("手动输入模式", null, (s, e) => OpenManual());
            _reissueMenuItem = new ToolStripMenuItem("重新发卡…", null, OnReissue);
            tools.DropDownItems.Add(_reissueMenuItem);
            tools.DropDownItems.Add("读取 TAG 内容", null, OnReadTag);
            tools.DropDownItems.Add("密钥备案", null, (s, e) =>
            {
                using (var dlg = new KeyArchiveForm(_store))
                {
                    dlg.ShowDialog(this);
                    RefreshKeyStatus();
                }
            });
            tools.DropDownItems.Add("发卡记录 / 作废", null, (s, e) =>
            {
                using (var dlg = new RecordQueryForm(_store, _operatorId))
                {
                    dlg.ShowDialog(this);
                }
            });
            tools.DropDownItems.Add("串口设置", null, (s, e) =>
                MessageBox.Show(this, "当前固定波特率 115200 8N1。请在主界面选择 COM 口后打开。", "串口设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Information));
            tools.DropDownItems.Add("打开数据目录", null, (s, e) => AppPaths.OpenDirectory(AppPaths.DataDirectory));

            var help = new ToolStripMenuItem("帮助(&H)");
            help.DropDownItems.Add("打开说明书", null, (s, e) => AboutForm.OpenManual());
            help.DropDownItems.Add("协议说明", null, (s, e) =>
            {
                string path = AppPaths.ProtocolDocPath;
                if (System.IO.File.Exists(path))
                {
                    System.Diagnostics.Process.Start(path);
                }
                else
                {
                    MessageBox.Show(this, "未找到 docs/protocol.md", "帮助", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            });
            help.DropDownItems.Add("关于", null, (s, e) =>
            {
                using (var dlg = new AboutForm())
                {
                    dlg.ShowDialog(this);
                }
            });

            menu.Items.Add(file);
            menu.Items.Add(tools);
            menu.Items.Add(help);
            MainMenuStrip = menu;
            Controls.Add(menu);
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(12, 12, 12, 8)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            root.Controls.Add(BuildTopBar(), 0, 0);
            root.Controls.Add(BuildCurrentPanel(), 0, 1);
            root.Controls.Add(BuildActionPanel(), 0, 2);
            root.Controls.Add(BuildRecentPanel(), 0, 3);

            var status = new StatusStrip();
            _keyStatus = new ToolStripStatusLabel("密钥备案: 未加载");
            _manualStatus = new ToolStripStatusLabel("手动模式: 锁定");
            status.Items.Add(_keyStatus);
            status.Items.Add(new ToolStripStatusLabel("|"));
            status.Items.Add(_manualStatus);
            status.Items.Add(new ToolStripStatusLabel { Spring = true, Text = "" });
            status.Items.Add(new ToolStripStatusLabel("本机库: " + AppPaths.DatabaseFile));

            Controls.Add(root);
            Controls.Add(status);
        }

        private Control BuildTopBar()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Panel };
            panel.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, panel.ClientRectangle, Color.Gainsboro, ButtonBorderStyle.Solid);

            var comLabel = new Label { Text = "串口:", AutoSize = true, Location = new Point(12, 16) };
            _ports = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(52, 12),
                Width = 110
            };
            _ports.DropDown += (s, e) => RefreshPorts(keepSelection: true);

            var baud = new Label { Text = "波特率: 115200", AutoSize = true, Location = new Point(175, 16) };
            var open = UiTheme.PrimaryButton("打开");
            open.Location = new Point(310, 10);
            open.Size = new Size(72, 28);
            open.Click += OnOpenPort;
            var close = UiTheme.SecondaryButton("关闭");
            close.Location = new Point(390, 10);
            close.Size = new Size(72, 28);
            close.Click += OnClosePort;

            _connDot = new Label
            {
                Text = "●",
                AutoSize = true,
                Font = UiTheme.Ui(12f, FontStyle.Bold),
                ForeColor = UiTheme.Disconnected,
                Location = new Point(480, 12)
            };
            _connText = new Label { Text = "未连接", AutoSize = true, Location = new Point(500, 16) };

            _operatorLabel = new Label
            {
                Text = "操作员: " + _operatorId,
                AutoSize = true,
                Location = new Point(12, 58),
                Font = UiTheme.Ui(9.5f, FontStyle.Bold)
            };
            _statsLabel = new Label
            {
                AutoSize = true,
                Location = new Point(220, 58),
                Text = StatsText()
            };

            panel.Controls.Add(comLabel);
            panel.Controls.Add(_ports);
            panel.Controls.Add(baud);
            panel.Controls.Add(open);
            panel.Controls.Add(close);
            panel.Controls.Add(_connDot);
            panel.Controls.Add(_connText);
            panel.Controls.Add(_operatorLabel);
            panel.Controls.Add(_statsLabel);
            return panel;
        }

        private Control BuildCurrentPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Panel, Padding = new Padding(12) };
            panel.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, panel.ClientRectangle, Color.Gainsboro, ButtonBorderStyle.Solid);

            var title = new Label
            {
                Text = "当前待发",
                Font = UiTheme.Ui(11f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(12, 10)
            };
            _mnValue = new Label { Text = "物料号: —", AutoSize = true, Location = new Point(12, 44), Font = UiTheme.Ui(12f) };
            _idValue = new Label { Text = "ID号: —", AutoSize = true, Location = new Point(280, 44), Font = UiTheme.Ui(12f) };
            _progressLabel = new Label { Text = "队列进度: 0 / 0", AutoSize = true, Location = new Point(12, 84) };

            var add = UiTheme.SecondaryButton("加入一条到队列…");
            add.Location = new Point(12, 110);
            add.Size = new Size(160, 28);
            add.Click += (s, e) => OpenManual();

            panel.Controls.Add(title);
            panel.Controls.Add(_mnValue);
            panel.Controls.Add(_idValue);
            panel.Controls.Add(_progressLabel);
            panel.Controls.Add(add);
            return panel;
        }

        private Control BuildActionPanel()
        {
            var host = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

            _statusBox = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(236, 240, 245), Margin = new Padding(0, 0, 8, 0) };
            _bigStatus = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = UiTheme.Ui(22f, FontStyle.Bold),
                ForeColor = UiTheme.Wait,
                Text = "请打开串口后放卡"
            };
            _statusBox.Controls.Add(_bigStatus);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(8)
            };
            _btnStart = UiTheme.PrimaryButton("开始自动发卡");
            _btnStart.Width = 160;
            _btnStart.Height = 40;
            _btnStart.Click += OnStart;
            _btnPause = UiTheme.SecondaryButton("暂停");
            _btnPause.Width = 160;
            _btnPause.Enabled = false;
            _btnPause.Click += OnPause;
            _btnSkip = UiTheme.SecondaryButton("跳过本条");
            _btnSkip.Width = 160;
            _btnSkip.Click += OnSkip;
            _btnReadTag = UiTheme.SecondaryButton("读取 TAG 内容");
            _btnReadTag.Width = 160;
            _btnReadTag.Click += OnReadTag;
            buttons.Controls.Add(_btnStart);
            buttons.Controls.Add(_btnPause);
            buttons.Controls.Add(_btnSkip);
            buttons.Controls.Add(_btnReadTag);

            host.Controls.Add(_statusBox, 0, 0);
            host.Controls.Add(buttons, 1, 0);
            return host;
        }

        private Control BuildRecentPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Panel };
            var title = new Label
            {
                Text = "最近结果",
                Dock = DockStyle.Top,
                Height = 24,
                Font = UiTheme.Ui(10f, FontStyle.Bold),
                Padding = new Padding(8, 4, 0, 0)
            };
            _recent = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            _recent.Columns.Add("时间", 90);
            _recent.Columns.Add("SN", 160);
            _recent.Columns.Add("物料号", 100);
            _recent.Columns.Add("ID", 70);
            _recent.Columns.Add("结果", 220);
            panel.Controls.Add(_recent);
            panel.Controls.Add(title);
            return panel;
        }

        private void RefreshPorts(bool keepSelection = false)
        {
            string selected = _ports.SelectedItem as string;
            _ports.Items.Clear();
            foreach (string name in Mh2020cReader.GetPortNames())
            {
                _ports.Items.Add(name);
            }

            if (keepSelection && selected != null && _ports.Items.Contains(selected))
            {
                _ports.SelectedItem = selected;
            }
            else if (_ports.Items.Count > 0)
            {
                _ports.SelectedIndex = 0;
            }
        }

        private void OnOpenPort(object sender, EventArgs e)
        {
            if (_ports.SelectedItem == null)
            {
                MessageBox.Show(this, "没有可用串口。请用 USB 连接读卡器后下拉刷新。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                _reader.Open(_ports.SelectedItem.ToString(), 115200);
                SetConnected(true, _ports.SelectedItem.ToString());
                SetBigStatus("已连接，请放卡 / 等待中…", UiTheme.Wait, Color.FromArgb(236, 240, 245));
            }
            catch (Exception ex)
            {
                SetConnected(false, null);
                MessageBox.Show(this, "打不开串口: " + ex.Message + "\r\n请检查 COM、占用和线缆。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnClosePort(object sender, EventArgs e)
        {
            OnPause(sender, e);
            _reader.Close();
            SetConnected(false, null);
            SetBigStatus("串口已关闭", UiTheme.Wait, Color.FromArgb(236, 240, 245));
        }

        private void SetConnected(bool connected, string port)
        {
            _connDot.ForeColor = connected ? UiTheme.Connected : UiTheme.Disconnected;
            _connText.Text = connected ? ("已连接" + (port == null ? "" : " " + port)) : "未连接";
        }

        private void OnStart(object sender, EventArgs e)
        {
            if (!_reader.IsOpen)
            {
                MessageBox.Show(this, "请先打开串口。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_queue.Count == 0)
            {
                MessageBox.Show(this, "队列为空。请先手动加入物料号 + ID。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_engine != null && _engine.IsRunning)
            {
                return;
            }

            if (_engine != null)
            {
                _engine.Dispose();
            }

            _queue.OriginalTotal = Math.Max(_queue.OriginalTotal, _queue.Count);
            _engine = new AutoWriteEngine(_queue, _store, _reader, _operatorId);
            _engine.Progress += OnEngineProgress;
            _engine.Start();
            _btnStart.Enabled = false;
            _btnPause.Enabled = true;
            SetBigStatus("请放卡 / 等待中…", UiTheme.Wait, Color.FromArgb(236, 240, 245));
        }

        private void OnPause(object sender, EventArgs e)
        {
            if (_engine != null)
            {
                _engine.Pause();
            }

            _btnStart.Enabled = true;
            _btnPause.Enabled = false;
        }


        private void OnReadTag(object sender, EventArgs e)
        {
            if (!_reader.IsOpen)
            {
                MessageBox.Show(this, "请先打开串口。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_engine != null && _engine.IsRunning)
            {
                MessageBox.Show(this, "自动发卡进行中，请先暂停后再读取 TAG。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string uid = null;
            try
            {
                CardPresence check = _reader.CheckCard(800);
                if (check.Present)
                {
                    uid = check.UidHex;
                }
            }
            catch
            {
            }

            ReadTagTextResult read;
            try
            {
                read = _reader.ReadTagText(5000);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "读取失败: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!read.Success)
            {
                MessageBox.Show(this, "读取 TAG 失败: " + (read.Message ?? "未知错误"), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string mn;
            string id;
            bool parsed = NdefText.TryParse(read.Text, out mn, out id);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(uid))
            {
                sb.AppendLine("UID: " + uid);
            }
            else
            {
                sb.AppendLine("UID: (未检测到 / 可选)");
            }
            sb.AppendLine();
            sb.AppendLine("原始文本:");
            sb.AppendLine(string.IsNullOrEmpty(read.Text) ? "(空)" : read.Text);
            sb.AppendLine();
            if (parsed)
            {
                sb.AppendLine("解析 MN: " + mn);
                sb.AppendLine("解析 ID: " + id);
            }
            else
            {
                sb.AppendLine("解析: 未能识别 MN/ID 格式");
            }

            MessageBox.Show(this, sb.ToString(), "读取 TAG 内容", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnSkip(object sender, EventArgs e)
        {
            if (_engine != null && _engine.IsRunning)
            {
                _engine.Skip();
            }
            else
            {
                _queue.SkipCurrentKeepInQueue();
                RefreshQueuePanel();
            }
        }

        private void OnEngineProgress(AutoWriteProgress p)
        {
            if (IsDisposed)
            {
                return;
            }

            RefreshQueuePanel();
            if (p.SuccessLook)
            {
                SetBigStatus(p.BigStatus, Color.White, UiTheme.Success);
            }
            else if (p.FailLook)
            {
                SetBigStatus(p.BigStatus, Color.White, UiTheme.Fail);
            }
            else
            {
                SetBigStatus(p.BigStatus, UiTheme.Wait, Color.FromArgb(236, 240, 245));
            }

            if (p.LastAttempt != null)
            {
                if (p.LastAttempt.Success)
                {
                    _shiftOk++;
                }
                else
                {
                    _shiftFail++;
                }

                AddRecent(p.LastAttempt);
            }

            _statsLabel.Text = StatsText();
            if (!p.Running)
            {
                _btnStart.Enabled = true;
                _btnPause.Enabled = false;
            }
        }

        private void AddRecent(WriteAttempt attempt)
        {
            var item = new ListViewItem(attempt.Time.ToString("HH:mm:ss"));
            item.SubItems.Add(attempt.SerialNumber ?? "");
            item.SubItems.Add(attempt.Item == null ? "" : attempt.Item.MaterialNumber);
            item.SubItems.Add(attempt.Item == null ? "" : attempt.Item.CardId);
            item.SubItems.Add(attempt.Message);
            item.ForeColor = attempt.Success ? UiTheme.Success : UiTheme.Fail;
            _recent.Items.Insert(0, item);
            while (_recent.Items.Count > 50)
            {
                _recent.Items.RemoveAt(_recent.Items.Count - 1);
            }
        }

        private void LoadRecentFromDb()
        {
            foreach (WriteLog log in _store.RecentLogs(20))
            {
                var item = new ListViewItem(log.WroteAt.ToString("HH:mm:ss"));
                item.SubItems.Add(log.SerialNumber ?? "");
                item.SubItems.Add(log.MaterialNumber);
                item.SubItems.Add(log.CardId);
                item.SubItems.Add(log.Detail ?? log.Result.ToString());
                item.ForeColor = log.Result == WriteResultKind.Success ? UiTheme.Success : UiTheme.Fail;
                _recent.Items.Add(item);
            }
        }

        private void RefreshQueuePanel()
        {
            QueueItem current = _queue.Peek();
            if (current == null)
            {
                _mnValue.Text = "物料号: —";
                _idValue.Text = "ID号: —";
            }
            else
            {
                _mnValue.Text = "物料号: " + current.MaterialNumber;
                _idValue.Text = "ID号: " + current.CardId;
            }

            int remaining = _queue.Count;
            int done = _shiftOk;
            int total = Math.Max(_queue.OriginalTotal, remaining + done);
            _progressLabel.Text = "队列进度: " + done + " / " + total + "    剩余 " + remaining;
            _statsLabel.Text = StatsText();
        }

        private string StatsText()
        {
            return "本班成功: " + _shiftOk + "    失败: " + _shiftFail + "    队列剩余: " + _queue.Count;
        }

        private void SetBigStatus(string text, Color fg, Color bg)
        {
            _bigStatus.Text = text;
            _bigStatus.ForeColor = fg;
            _statusBox.BackColor = bg;
        }

        private void OnReissue(object sender, EventArgs e)
        {
            if (!_reader.IsOpen)
            {
                MessageBox.Show(this, "请先打开串口。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_engine != null && _engine.IsRunning)
            {
                MessageBox.Show(this, "请先暂停自动发卡，再使用重新发卡。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            QueueItem current = _queue.Peek();
            if (current == null)
            {
                MessageBox.Show(this, "队列为空。请先加入要重新写入的物料号 + ID。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_store.IsIdReserved(current.CardId))
            {
                MessageBox.Show(this, "队列中的 ID 已在库中占用。请换一个未用过的 ID。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(this, "请放上已发过卡的 TAG，然后点击确定开始识别。", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            CardPresence card;
            try
            {
                card = _reader.CheckCard(2500);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "寻卡失败: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!card.Present || string.IsNullOrEmpty(card.UidHex))
            {
                MessageBox.Show(this, "未检测到卡。请放好 TAG 后重试。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string sn = card.UidHex;
            if (!_store.IsSnIssued(sn))
            {
                MessageBox.Show(this, "该卡（SN " + sn + "）不在已发卡库中，请用「开始自动发卡」。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult ask = MessageBox.Show(this,
                "该卡已发卡（SN " + sn + "）。" + Environment.NewLine
                + "删除原记录后才能重新发卡。" + Environment.NewLine
                + "将写入：MN " + current.MaterialNumber + " / ID " + current.CardId + Environment.NewLine
                + Environment.NewLine
                + "确定删除原记录并继续重新发卡吗？",
                "重新发卡", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (ask != DialogResult.OK)
            {
                return;
            }

            if (!PasswordForm.TryPromptTwice(this))
            {
                MessageBox.Show(this, "密码未通过，已取消重新发卡。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int removed = _store.DeleteIssuedBySn(sn, _operatorId);
            if (removed <= 0)
            {
                MessageBox.Show(this, "删除失败：库中未找到该 SN 记录。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetBigStatus("重新发卡中…", UiTheme.Wait, Color.FromArgb(236, 240, 245));
            Application.DoEvents();

            ProvisionResult provision;
            try
            {
                provision = _reader.Provision(current.MaterialNumber, current.CardId, 8000);
            }
            catch (Exception ex)
            {
                string msg = "重新发卡失败: " + ex.Message;
                _store.RecordFailure(current.CardId, current.MaterialNumber, sn, _operatorId, DateTime.Now, msg);
                NoteReissueAttempt(current, sn, false, msg);
                MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!string.IsNullOrEmpty(provision.UidHex))
            {
                sn = provision.UidHex;
            }

            DateTime now = DateTime.Now;
            if (!provision.Success)
            {
                _store.RecordFailure(current.CardId, current.MaterialNumber, sn, _operatorId, now, provision.Message);
                NoteReissueAttempt(current, sn, false, provision.Message);
                MessageBox.Show(this, provision.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ReadTagTextResult readBack = _reader.ReadTagText(5000);
            if (!readBack.Success || !NdefText.Matches(current.MaterialNumber, current.CardId, readBack.Text))
            {
                string failMsg = "失败: 读回比对失败";
                if (!readBack.Success)
                {
                    failMsg = failMsg + " (" + (readBack.Message ?? "读回失败") + ")";
                }

                _store.RecordFailure(current.CardId, current.MaterialNumber, sn, _operatorId, now, failMsg);
                NoteReissueAttempt(current, sn, false, failMsg);
                MessageBox.Show(this, failMsg, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                _store.RecordSuccess(current.CardId, current.MaterialNumber, sn, _operatorId, now);
            }
            catch (Exception ex)
            {
                _store.RecordFailure(current.CardId, current.MaterialNumber, sn, _operatorId, now, ex.Message);
                NoteReissueAttempt(current, sn, false, ex.Message);
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _queue.Dequeue();
            NoteReissueAttempt(current, sn, true, "成功");
            SetBigStatus("重新发卡成功", Color.White, UiTheme.Success);
            MessageBox.Show(this, "重新发卡成功。" + Environment.NewLine + "SN " + sn
                + Environment.NewLine + "MN " + current.MaterialNumber + " / ID " + current.CardId,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void NoteReissueAttempt(QueueItem item, string sn, bool success, string message)
        {
            if (success)
            {
                _shiftOk++;
            }
            else
            {
                _shiftFail++;
            }

            AddRecent(new WriteAttempt
            {
                Item = item,
                SerialNumber = sn,
                Success = success,
                Message = message,
                Time = DateTime.Now
            });
            RefreshQueuePanel();
        }

        private void OpenManual()
        {
            if (!_manualUnlocked)
            {
                using (var pwd = new PasswordForm())
                {
                    if (pwd.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                }

                _manualUnlocked = true;
                _manualStatus.Text = "手动模式: 已解锁";
            }

            using (var dlg = new ManualInputForm())
            {
                DialogResult result = dlg.ShowDialog(this);
                if (result == DialogResult.Abort)
                {
                    _manualUnlocked = false;
                    _manualStatus.Text = "手动模式: 锁定";
                    return;
                }

                if (result != DialogResult.OK || dlg.Item == null)
                {
                    return;
                }

                if (_store.IsIdReserved(dlg.Item.CardId))
                {
                    MessageBox.Show(this, "该 ID 已在本地库（含已作废），不能再发。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (dlg.DirectWrite)
                {
                    _queue.EnqueueFront(dlg.Item);
                    if (_reader.IsOpen && (_engine == null || !_engine.IsRunning))
                    {
                        OnStart(this, EventArgs.Empty);
                    }
                }
                else
                {
                    _queue.Enqueue(dlg.Item);
                }

                if (_queue.OriginalTotal < _queue.Count)
                {
                    _queue.OriginalTotal = _queue.Count;
                }

                RefreshQueuePanel();
            }
        }

        private void RefreshKeyStatus()
        {
            KeyArchiveInfo info = _store.GetKeyArchive();
            if (info == null)
            {
                _keyStatus.Text = "密钥备案: 未加载";
            }
            else
            {
                _keyStatus.Text = "密钥备案: " + System.IO.Path.GetFileName(info.FilePath) + " (已加载·仅备案)";
            }
        }


        private void OnImportList()
        {
            if (_engine != null && _engine.IsRunning)
            {
                MessageBox.Show(this, "自动发卡进行中，请先暂停后再导入清单。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_queue.Count > 0)
            {
                DialogResult ask = MessageBox.Show(this,
                    "当前队列仍有 " + _queue.Count + " 条待发。" + Environment.NewLine
                    + "是否将导入的合法行追加到队列末尾？" + Environment.NewLine
                    + "选「否」将取消本次导入（不清空现有队列）。",
                    "导入清单", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ask != DialogResult.Yes)
                {
                    return;
                }
            }

            using (var dlg = new ImportListForm(_store, _queue))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.AcceptedItems == null)
                {
                    return;
                }

                int n = 0;
                foreach (QueueItem item in dlg.AcceptedItems)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    _queue.Enqueue(item);
                    n++;
                }

                if (_queue.OriginalTotal < _queue.Count)
                {
                    _queue.OriginalTotal = _queue.Count;
                }

                RefreshQueuePanel();
                MessageBox.Show(this, "已追加 " + n + " 条到队列。请手动点「开始自动发卡」。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OnExportRecords()
        {
            IList<IssuedCard> rows = _store.QueryIssued("", "", "", "");
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "导出发卡记录";
                dlg.Filter = "CSV (*.csv)|*.csv";
                dlg.DefaultExt = "csv";
                dlg.FileName = RecordExport.DefaultFileName();
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    RecordExport.WriteIssuedCsv(rows, dlg.FileName);
                    MessageBox.Show(this,
                        "已导出 " + rows.Count + " 条到：" + Environment.NewLine + dlg.FileName
                        + Environment.NewLine + "（最多 500 条，与查询上限相同）",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败: " + ex.Message, Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (_engine != null)
                {
                    _engine.Dispose();
                }

                _reader.Dispose();
                _store.Dispose();
            }
            catch
            {
            }
        }
    }
}
