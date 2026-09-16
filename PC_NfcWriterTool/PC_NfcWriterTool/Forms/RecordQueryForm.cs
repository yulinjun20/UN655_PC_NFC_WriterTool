using System;
using System.Drawing;
using System.Windows.Forms;
using PC_NfcWriterTool.Data;

namespace PC_NfcWriterTool.Forms
{
    /// <summary>
    /// M2 stub with a working query list. Void/export remain clearly marked TODO.
    /// </summary>
    public sealed class RecordQueryForm : Form
    {
        private readonly CardStore _store;
        private readonly string _operatorId;
        private readonly TextBox _sn;
        private readonly TextBox _mn;
        private readonly TextBox _id;
        private readonly TextBox _op;
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

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 48,
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
            export.Click += (s, e) => StubDialogs.NotInM1("导出Excel（M2）");
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
            foreach (IssuedCard row in _store.QueryIssued(_sn.Text, _mn.Text, _id.Text, _op.Text))
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

        private void OnVoid(object sender, EventArgs e)
        {
            if (_list.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "请先选中一条记录。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var row = (IssuedCard)_list.SelectedItems[0].Tag;
            if (MessageBox.Show(this, "作废后该 ID 永远不能再发给任何新卡。确认作废 " + row.CardId + " ?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
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
