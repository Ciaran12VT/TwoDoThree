using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Wpf;
using TwoDoThree.Services;

internal static class PinLayoutChecks
{
    public static async Task Run(WebView2 web, WebView2 other, string path)
    {
        async Task<string> Js(string s) => await web.ExecuteScriptAsync(s);
        async Task<double> Number(string s) => double.Parse(await Js(s), System.Globalization.CultureInfo.InvariantCulture);
        var source = File.ReadAllBytes(path);
        Check(await Js("pinToggle.textContent.trim()==='' && !!pinToggle.querySelector('svg') && !!pinToggle.getAttribute('aria-label')") == "true", "Sidebar toggle is an accessible hamburger icon");
        Check(await Js("Array.from(document.querySelectorAll('.pin-unpin')).every(b=>b.textContent==='' && !!b.title && !!b.getAttribute('aria-label'))") == "true", "Unpin controls are labelled icon buttons");
        Check(await Js("document.getElementById('pin-menu').hidden") == "true", "Pin-selection command is unobtrusive in the context menu");
        await Js("if(sidebarOpen)pinToggle.click()");
        Check(await Js("getComputedStyle(pinDivider).display") == "\"none\"", "Closed sidebar has no visible or active divider");
        await Js("pinToggle.click();document.getElementById('section-40').scrollIntoView()");
        var anchor = await Number("captureMarkdownAnchor().line");
        async Task Resize(double x)
        {
            var current = await Number("pinDivider.getBoundingClientRect().left+3");
            await PinChecks.Mouse(web,"mousePressed",current,150,1);
            await PinChecks.Mouse(web,"mouseMoved",x,150,1);
            await PinChecks.Mouse(web,"mouseReleased",x,150,0);
            await Task.Delay(120);
        }
        await Resize(50);
        Check(Math.Abs(await Number("pinSidebar.getBoundingClientRect().width")-200)<1,"Divider enforces the sidebar minimum width");
        await Resize(await Number("innerWidth-5"));
        Check(await Number("innerWidth-pinSidebar.getBoundingClientRect().width")>=279,"Divider leaves readable room for the main document");
        await Resize(400);
        Check(Math.Abs(await Number("captureMarkdownAnchor().line")-anchor)<2,"Resizing preserves the main reading passage");
        Check(new MarkdownPinStore().Load(path).SidebarWidth==400,"Sidebar width persists per document");
        await Js("pinToggle.click();pinToggle.click()");
        Check(await Number("pinSidebar.getBoundingClientRect().width")==400,"Sidebar width survives close and reopen");
        Check(await other.ExecuteScriptAsync("preferredWidth") == "400", "Other views receive saved sidebar preferences");

        foreach (var selector in new[]{"#long-excerpts + p", "#long-excerpts + p + ul", "#long-excerpts + p + ul + [data-run]"})
        {
            int prior=MarkdownDocumentSession.Get(path).Pins.Count;
            await Js($"{{const e=document.querySelector({JsonSerializer.Serialize(selector)});const r=document.createRange();r.selectNodeContents(e);submitPin(selectionPayload(r));}}");
            for(int i=0;i<100 && MarkdownDocumentSession.Get(path).Pins.Count==prior;i++)await Task.Delay(30);
            Check(MarkdownDocumentSession.Get(path).Pins.Count==prior+1,"Long excerpt is pinned for wrap validation");
        }
        await Task.Delay(100);
        var mainWrap=await Js("getComputedStyle(document.querySelector('#markdown-main pre')).whiteSpace");
        Check(await Js("Array.from(pinList.querySelectorAll('.pin-card')).slice(-3).every(c=>{const e=c.querySelector('.pin-content');return e.scrollWidth<=e.clientWidth+2})") == "true", "Wrapped long prose, list and code fit the sidebar");
        await Js("pinWrap.click()"); await Task.Delay(150);
        Check(await Js("Array.from(pinList.querySelectorAll('.pin-card')).slice(-3).every(c=>{const e=c.querySelector('.pin-content');return e.scrollWidth>e.clientWidth})") == "true", "Unwrapped prose, list and code have horizontal scrolling");
        await Js("{const e=pinList.lastElementChild.querySelector('.pin-content');e.scrollLeft=100}");
        Check(await Number("pinList.lastElementChild.querySelector('.pin-content').scrollLeft")>0,"Unwrapped content scrolls horizontally without truncation");
        await Js("renderPins()");
        Check(await Number("pinList.lastElementChild.querySelector('.pin-content').scrollLeft")==100,"Pin refresh preserves horizontal reading position");
        Check(await Js("!!pinList.lastElementChild.querySelector('.copy-code')") == "true", "Unwrapped code retains its copy control");
        Check(mainWrap==await Js("getComputedStyle(document.querySelector('#markdown-main pre')).whiteSpace"),"Pin wrapping leaves main-document wrapping unchanged");
        Check(!new MarkdownDocumentSession(path,new MarkdownPinStore()).Wrap,"Wrapping choice survives reopening the document");
        Check(await other.ExecuteScriptAsync("pinWrap.checked")=="false","Other views receive the wrapping choice");
        Check(File.ReadAllBytes(path).SequenceEqual(source),"Sidebar layout preferences never change source bytes");
        await Js("pinWrap.click()"); await Resize(330);
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
