using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PC_NfcWriterTool.Data;
using PC_NfcWriterTool.Services;

namespace PC_NfcWriterTool.Forms
{
    /// <summary>
    /// Query issued cards; void selected; export current filter as UTF-8 BOM CSV.
    /// </summary>
    public sealed class RecordQueryForm : Form
    {
        private readonly CardStore _store;
        private readonly string _operatorId;
        private readonly TextBox _sn;
        private readonly TextBox _mn;
        private readonly TextBox _id;
        private readonly TextBox _op;
        private readonly ComboBox _status;
        private readonly ListView _list;

        public RecordQueryForm(CardStore store, string operatorId)
        {
            _store = store;
            _operatorId = operatorId;
            Text = "发卡记录";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(820, 480);
            BackColor = UiTheme.Bg;
            Font = UiTheme.Ui(9.5f);

            _sn = Box();
            _mn = Box();
            _id = Box();
            _op = Box();
            _status = new ComboBox
            {
                Width = 80,
                Height = 24,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _status.Items.AddRange(new object[] { "全部", "成功", "已作废" });
            _status.SelectedIndex = 0;

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 54,
                Padding = new Padding(8)
            };
            bar.Controls.Add(new Label { Text = "SN", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
            bar.Controls.Add(_sn);
            bar.Controls.Add(new Label { Text = "物料号", AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
            bar.Controls.Add(_mn);
            bar.Controls.Add(new Label { Text = "ID", AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
            bar.Controls.Add(_id);
            bar.Controls.Add(new Label { Text = "工号", AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
            bar.Controls.Add(_op);
            bar.Controls.Add(new Label { Text = "状态", AutoSize = true, Padding = new Padding(8, 8, 0, 0) });
            bar.Controls.Add(_status);
            var query = UiTheme.PrimaryButton("查询");
            query.Width = 80;
            query.Height = 30;
            query.Click += (s, e) => Reload();
            bar.Controls.Add(query);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            _list.Columns.Add("SN", 140);
            _list.Columns.Add("物料号", 100);
            _list.Columns.Add("ID", 70);
            _list.Columns.Add("工号", 90);
            _list.Columns.Add("时间", 150);
            _list.Columns.Add("状态", 80);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                Padding = new Padding(8),
                FlowDirection = FlowDirection.RightToLeft
            };
            var export = UiTheme.SecondaryButton("导出Excel");
            export.Width = 110;
            export.Click += OnExport;
            var voidBtn = UiTheme.SecondaryButton("作废选中");
            voidBtn.Width = 110;
            voidBtn.Click += OnVoid;
            bottom.Controls.Add(export);
            bottom.Controls.Add(voidBtn);

            Controls.Add(_list);
            Controls.Add(bottom);
            Controls.Add(bar);
            Reload();
        }

        private static TextBox Box()
        {
            return new TextBox { Width = 90, Height = 24 };
        }

        private void Reload()
        {
            _list.Items.Clear();
            IssueStatus? statusFilter = null;
            if (_status.SelectedIndex == 1)
            {
                statusFilter = IssueStatus.Success;
            }
            else if (_status.SelectedIndex == 2)
            {
                statusFilter = IssueStatus.Voided;
            }

            foreach (IssuedCard row in _store.QueryIssued(_sn.Text, _mn.Text, _id.Text, _op.Text, statusFilter))
            {
                var item = new ListViewItem(row.SerialNumber ?? "");
                item.SubItems.Add(row.MaterialNumber);
                item.SubItems.Add(row.CardId);
                item.SubItems.Add(row.OperatorId);
                item.SubItems.Add(row.IssuedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(row.Status == IssueStatus.Voided ? "已作废" : "成功");
                item.Tag = row;
                _list.Items.Add(item);
            }
        }


        private void OnExport(object sender, EventArgs e)
        {
            var rows = new List<IssuedCard>();
            foreach (ListViewItem item in _list.Items)
            {
                var row = item.Tag as IssuedCard;
                if (row != null)
                {
                    rows.Add(row);
                }
            }

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
                        "已导出 " + rows.Count + " 条到：" + Environment.NewLine + dlg.FileName,
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败: " + ex.Message, Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void OnVoid(object sender, EventArgs e)
        {
            if (_list.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "请先选中一条记录。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var row = (IssuedCard)_list.SelectedItems[0].Tag;
            if (row.Status == IssueStatus.Voided)
            {
                MessageBox.Show(this, "该 ID 已作废，无需重复操作。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this, "作废后该 ID 永远不能再发给任何新卡。确认作废 " + row.CardId + " ?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            if (!PasswordForm.TryPrompt(this, "作废确认"))
            {
                return;
            }

            try
            {
                _store.VoidId(row.CardId, _operatorId);
                Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
