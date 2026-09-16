using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PC_NfcWriterTool.Data;
using PC_NfcWriterTool.Services;

namespace PC_NfcWriterTool.Forms
{
    public sealed class ImportListForm : Form
    {
        private readonly CardStore _store;
        private readonly HashSet<string> _queueIds;
        private readonly Label _fileLabel;
        private readonly Label _statsLabel;
        private readonly ListView _list;
        private readonly Button _btnImport;
        private ImportParseResult _parsed;

        /// <summary>Accepted queue items after OK (合法行 only).</summary>
        public IList<QueueItem> AcceptedItems { get; private set; }

        public ImportListForm(CardStore store, PendingQueue queue)
            : this(store, BuildQueueIdSet(queue))
        {
        }

        public ImportListForm(CardStore store, ISet<string> queueIds)
        {
            _store = store;
            _queueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (queueIds != null)
            {
                foreach (string id in queueIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        _queueIds.Add(id);
                    }
                }
            }

            AcceptedItems = new List<QueueItem>();

            Text = "导入待发清单";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 480);
            Size = new Size(720, 520);
            BackColor = UiTheme.Bg;
            Font = UiTheme.Ui(9.5f);

            var top = new Panel
            {
                Dock = DockStyle.Top,
                Height = 78,
                Padding = new Padding(12, 10, 12, 4)
            };

            var pick = UiTheme.SecondaryButton("选择文件…");
            pick.Location = new Point(12, 10);
            pick.Size = new Size(110, 32);
            pick.Click += OnPickFile;
            top.Controls.Add(pick);

            _fileLabel = new Label
            {
                AutoSize = true,
                Location = new Point(130, 16),
                Text = "未选择文件",
                ForeColor = UiTheme.Wait
            };
            top.Controls.Add(_fileLabel);

            _statsLabel = new Label
            {
                AutoSize = true,
                Location = new Point(12, 48),
                Text = "解析结果: —"
            };
            top.Controls.Add(_statsLabel);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false
            };
            _list.Columns.Add("行", 50);
            _list.Columns.Add("物料号", 120);
            _list.Columns.Add("ID号", 90);
            _list.Columns.Add("状态", 160);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                Padding = new Padding(12, 10, 12, 10),
                FlowDirection = FlowDirection.RightToLeft
            };

            var cancel = UiTheme.SecondaryButton("取消");
            cancel.Width = 100;
            cancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            bottom.Controls.Add(cancel);

            _btnImport = UiTheme.PrimaryButton("仅导入合法行到队列");
            _btnImport.Width = 200;
            _btnImport.Enabled = false;
            _btnImport.Click += OnImport;
            bottom.Controls.Add(_btnImport);

            Controls.Add(_list);
            Controls.Add(bottom);
            Controls.Add(top);
        }

        private static HashSet<string> BuildQueueIdSet(PendingQueue queue)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (queue == null)
            {
                return set;
            }

            foreach (QueueItem item in queue.Snapshot())
            {
                if (item != null && !string.IsNullOrEmpty(item.CardId))
                {
                    set.Add(item.CardId);
                }
            }

            return set;
        }

        private void OnPickFile(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择待发清单";
                dlg.Filter = "清单文件 (*.csv;*.txt;*.xlsx)|*.csv;*.txt;*.xlsx|CSV (*.csv)|*.csv|TXT (*.txt)|*.txt|Excel (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                LoadFile(dlg.FileName);
            }
        }

        private void LoadFile(string path)
        {
            try
            {
                _parsed = ImportListParser.Parse(path, _store, _queueIds);
            }
            catch (Exception ex)
            {
                _parsed = null;
                _list.Items.Clear();
                _statsLabel.Text = "解析结果: —";
                _btnImport.Enabled = false;
                _fileLabel.Text = System.IO.Path.GetFileName(path);
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _fileLabel.Text = System.IO.Path.GetFileName(path);
            _fileLabel.ForeColor = Color.FromArgb(30, 40, 50);
            BindPreview();
        }

        private void BindPreview()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            if (_parsed != null)
            {
                foreach (ImportRowResult row in _parsed.Rows)
                {
                    var item = new ListViewItem(row.LineNumber.ToString());
                    item.SubItems.Add(row.MaterialNumber ?? "");
                    item.SubItems.Add(row.CardId ?? "");
                    item.SubItems.Add(row.Status ?? "");
                    if (row.IsAcceptable)
                    {
                        item.ForeColor = UiTheme.Success;
                    }
                    else
                    {
                        item.ForeColor = UiTheme.Fail;
                    }

                    _list.Items.Add(item);
                }

                _statsLabel.Text = string.Format(
                    "解析结果: 共 {0} 行  合法 {1}  格式错误 {2}  库内重复 {3}  文件内重复 {4}  队列已有 {5}",
                    _parsed.Total,
                    _parsed.ValidCount,
                    _parsed.FormatErrorCount,
                    _parsed.DbDupCount,
                    _parsed.FileDupCount,
                    _parsed.QueueDupCount);
                _btnImport.Enabled = _parsed.ValidCount > 0;
            }

            _list.EndUpdate();
        }

        private void OnImport(object sender, EventArgs e)
        {
            if (_parsed == null || _parsed.ValidCount == 0)
            {
                MessageBox.Show(this, "没有可导入的合法行。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var list = new List<QueueItem>();
            foreach (ImportRowResult row in _parsed.Rows)
            {
                if (!row.IsAcceptable)
                {
                    continue;
                }

                list.Add(new QueueItem
                {
                    MaterialNumber = row.MaterialNumber,
                    CardId = row.CardId,
                    Source = "导入"
                });
            }

            AcceptedItems = list;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
