using LabSchedule.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace LabSchedule;

/// <summary>
/// Prints the generated HTML to PDF with WebView2, the same Chromium engine the Electron build
/// used for printToPDF, so the PDF matches the HTML exactly.
/// </summary>
internal static class PdfPrinter
{
    private const double MillimetersPerInch = 25.4;

    public static async Task PrintAsync(Panel host, string htmlPath, string pdfPath, ScheduleSettings settings)
    {
        var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabSchedule", "WebView2");
        Directory.CreateDirectory(userDataFolder);
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, new CoreWebView2EnvironmentOptions());

        var webView = new WebView2 { Width = host.Width, Height = host.Height };
        host.Children.Add(webView);
        try
        {
            await webView.EnsureCoreWebView2Async(environment);
            var core = webView.CoreWebView2;

            var navigated = new TaskCompletionSource<bool>();
            core.NavigationCompleted += (_, args) => navigated.TrySetResult(args.IsSuccess);
            core.Navigate(new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri);
            if (!await navigated.Task) throw new InvalidOperationException("无法加载生成的 HTML");

            var landscape = settings.Orientation == "landscape";
            var (shortSide, longSide) = settings.PageSize == "A3" ? (297.0, 420.0) : (210.0, 297.0);
            var print = core.Environment.CreatePrintSettings();
            print.Orientation = landscape ? CoreWebView2PrintOrientation.Landscape : CoreWebView2PrintOrientation.Portrait;
            print.PageWidth = shortSide / MillimetersPerInch;
            print.PageHeight = longSide / MillimetersPerInch;
            // The page margin comes from the HTML's own @page rule (7mm), as with Electron's preferCSSPageSize.
            print.MarginTop = print.MarginBottom = print.MarginLeft = print.MarginRight = 0;
            print.ShouldPrintBackgrounds = true;
            print.ShouldPrintHeaderAndFooter = false;

            ScheduleStore.EnsureParentDir(pdfPath);
            if (!await core.PrintToPdfAsync(Path.GetFullPath(pdfPath), print))
            {
                throw new IOException($"PDF 写入失败，文件可能正被其他程序占用：{pdfPath}");
            }
        }
        finally
        {
            host.Children.Remove(webView);
            webView.Close();
        }
    }
}
