# 实验排班生成器

编辑年度实验/维护安排，保存默认 JSON、导入/导出 JSON，并生成可打印的 HTML/PDF 年度日历。
3.0.0 起使用原生 WinUI 3 界面重写，功能与 2.0.0（Electron 版）保持一致。

- 界面：WinUI 3（Windows App SDK 2.x），Mica 背景、系统标题栏按钮、跟随系统深浅色。
- 逻辑：`src/LabSchedule.Core` 是 2.0.0 版 `core.js` / `history.js` 的逐行移植，
  生成的 HTML 和保存的 JSON 与原版逐字节一致（见 `tests/`）。
- PDF：用 WebView2（与 Electron 相同的 Chromium 内核）打印 HTML，页面大小与边距由 HTML 的 `@page` 决定。

## 下载

在 [Releases](https://github.com/arcxya09/lab_scheduling/releases) 页面下载：

- `LabSchedule-<版本>-win-x64-setup.msi`：安装版。默认装到当前用户（`%LocalAppData%\Programs\实验排班生成器`），
  不需要管理员权限，会创建开始菜单和桌面快捷方式，可在"设置 → 应用"中卸载。
- `LabSchedule-<版本>-win-x64-portable.zip`：便携版。解压后运行 `实验排班生成器-win-x64\LabSchedule.exe`，
  请保留整个文件夹。

两者都自带 .NET 运行时和 Windows App SDK，支持 Windows 10 1809 及以上 / Windows 11 x64。
生成 PDF 需要 Microsoft Edge WebView2 运行时（Windows 11 自带）。

## 自动更新与设置

- 程序启动时会检查 GitHub 上的最新 Release，有新版本时在窗口顶部提示。点"立即更新"后下载对应文件
  （安装版下载 msi，便携版下载 zip），校验大小和 SHA-256，然后关闭程序、安装更新并自动重新打开。
  安装版是否为安装版由安装包写入的 `HKCU\Software\LabSchedule\InstallFolder` 判断。
- 命令栏右侧的"设置"可以切换主题、设置"联动后续安排"的默认状态、更改默认输出文件夹、开关自动检查更新。
  设置保存在 `%LocalAppData%\LabSchedule\settings.json`；程序崩溃时详细信息写入同目录的 `crash.log`。

## 目录

```text
src/LabSchedule.Core/         排班模型、排序编号、校验、HTML 生成、JSON 读写、撤销历史、时间轴布局（无界面依赖）
src/LabSchedule.App/          WinUI 3 程序（MainWindow、年度时间轴控件、PDF 打印）
tests/LabSchedule.Core.Tests/ xUnit 测试，含与 2.0.0 版输出的对照样本
installer/                    WiX 5 安装包工程
scripts/                      打包脚本
.github/workflows/            推送版本标签时自动编译并发布 Release
docs/releases/                各版本的发布说明
samples/                      示例排班 JSON，可用"导入 JSON"打开
backups/                      旧版本（Electron 2.0.0 等），只在本地保留，不进仓库
```

## 开发环境

需要 .NET 10 SDK（`dotnet --list-sdks` 能看到 10.0.x）。不需要 Visual Studio；
用 Visual Studio 2022/2026 打开 `LabSchedule.slnx` 也可以直接调试。

```powershell
# 运行
dotnet run --project src/LabSchedule.App -p:Platform=x64

# 测试
dotnet test tests/LabSchedule.Core.Tests

# 打包文件夹版：dist/<主版本.小版本>/实验排班生成器-win-x64/LabSchedule.exe
powershell -ExecutionPolicy Bypass -File scripts/publish-win.ps1

# 打包发布文件（便携版 zip + 安装版 msi）：dist/release/
powershell -ExecutionPolicy Bypass -File scripts/release-win.ps1
```

## 发布新版本

发布由 GitHub Actions（`.github/workflows/release.yml`）完成：

1. 修改 `src/LabSchedule.App/LabSchedule.App.csproj` 里的 `<Version>`，可选地在 `docs/releases/v<版本>.md` 写发布说明。
2. 提交后推送同名标签，例如 `git tag v3.0.1 && git push origin v3.0.1`。
3. Actions 会在 Windows 上运行测试、打包便携版和安装版，并创建 Release 上传这两个文件。
   也可以在 Actions 页面手动运行 "Release"，输入标签名。

## JSON 保存规则

- `保存 JSON`：写入默认文件 `文档/实验排班输出/default_schedule.json`，程序启动时自动加载它。
- `导出 JSON`：弹出保存对话框，可选择自定义文件夹和文件名。
- 列表、PDF 实验列表、保存/导出的 JSON 均按开始日期自动排序并重新编号。

## 测试用环境变量

开发和测试时可用以下环境变量，避免改动真实数据或真实安装：

| 变量 | 作用 |
| --- | --- |
| `LAB_SCHEDULE_OUTPUT_DIR` | 替换默认输出文件夹 |
| `LAB_SCHEDULE_SETTINGS_FILE` | 替换设置文件位置 |
| `LAB_SCHEDULE_UPDATE_FEED` | 替换更新检查地址（指向一个与 GitHub "latest release" 格式相同的 JSON）；Debug 版只有设置了它才会自动检查更新 |
| `LAB_SCHEDULE_REGISTRY_KEY` | 替换安装版注册表键，配合安装包的 `-p:AppRegistryKey=... -p:UpgradeCode=... -p:ProductTitle=...` 构建独立的测试安装 |
| `LAB_SCHEDULE_THEME` | 仅 Debug 版：`Dark` / `Light` 强制深浅色 |
