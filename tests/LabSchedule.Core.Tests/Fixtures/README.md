# 对照样本

这些文件由 Electron 2.0.0 版的 `core.js`（本地备份位于 `backups/electron_app-v2.0.0/src/core.js`） 生成，用来确认 C# 移植的输出与原版完全一致：

- `<name>.input.json`：输入
- `<name>.issues.json`：`validateSchedule` 的结果
- `<name>.prepared.json`：`JSON.stringify(prepareSchedule(input), null, 2) + "\n"`，即保存/导出的文件内容
- `<name>.expected.html` / `<name>.warnings.json`：`buildOutputHtmlResult(normalizeSchedule(input))`
- `invalid.cases.json`：一组无效文件及其校验提示

修改输出格式时，请同步更新这里的样本。
