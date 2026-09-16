发卡导入模板

列名必须为：物料号,ID号（首行表头，逗号分隔）
物料号：9 位数字
ID号：4 位字母或数字（导入后自动转大写）

支持格式：
- .csv / .txt：UTF-8（可带 BOM），逗号分隔
- .xlsx：ExcelDataReader 读取第一个工作表的前两列（同表头）

本目录主推 CSV/TXT 模板（import_template.csv / import_template.txt，
以及中文名「发卡导入模板.csv/.txt」）。
若需要 xlsx：用 Excel 打开 CSV 后另存为 .xlsx 即可；前两列保持「物料号」「ID号」。
注意：Excel 可能把纯数字 ID 去掉前导零，建议 ID 列设为文本格式。

导出记录为 UTF-8 BOM 的 CSV（扩展名 .csv），不是真正的 xlsx。
