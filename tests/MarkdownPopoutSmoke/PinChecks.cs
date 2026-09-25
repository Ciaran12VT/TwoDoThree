using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using Microsoft.Web.WebView2.Wpf;
using TwoDoThree.Controls;
using TwoDoThree.Services;

internal static class PinChecks
{
    public static async Task Run(FileResourcePreviewControl preview, WebView2 web, WebView2 other, string path)
    {
        async Task<string> Js(string script) => await web.ExecuteScriptAsync(script);
        var originalSource = File.ReadAllText(path);
        async Task<int> Count() => int.Parse(await Js("document.querySelectorAll('.pin-card').length"));
        async Task Select(string selector, int start, int length)
        {
            await Js($"{{const e=document.querySelector({JsonSerializer.Serialize(selector)}); const n=mappedNodes(e)[0]; const r=document.createRange(); r.setStart(n,{start});r.setEnd(n,{start+length});getSelection().removeAllRanges();getSelection().addRange(r);}}");
            await Task.Delay(40);
        }
        async Task Pin()
        {
            var count = await Count();
            await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", "{\"type\":\"keyDown\",\"key\":\"P\",\"code\":\"KeyP\",\"modifiers\":9}");
            await Until(async () => await Count() == count + 1, async () => await Js("document.getElementById('pin-status').textContent"));
        }
        await Select("#section-20 + p strong [data-run]", 3, 11);
        await Pin();
        Check(await Js("document.querySelector('.pin-content strong').textContent") == "\"matted pass\"", "Partial pin preserves bold formatting without widening selected text");
        Check(await Js("CSS.highlights.has('markdown-pins')") == "true", "Open sidebar highlights exact pin ranges");
        Check(new MarkdownDocumentSession(path, new MarkdownPinStore()).Pins.Count == 1, "Pin persists independently of the viewer session");
        await Until(async () => await other.ExecuteScriptAsync("pins.length") == "1");
        Check(true, "Another expanded window receives the same file's pin");

        await Select("#markdown-main li [data-run]", 0, 10);
        await Pin();
        await Select("#markdown-main li [data-run]", 0, 5);
        await Pin();
        Check(await Js("document.querySelectorAll('#pin-sidebar .markdown-task').length") == "2", "Overlapping checklist excerpts retain functional checkboxes");
        await Js("scrollTo(0,1200);document.getElementById('pin-sidebar').scrollTop=40");
        var before = await Js("JSON.stringify([scrollY,document.getElementById('pin-sidebar').scrollTop])");
        await Js("document.querySelector('#pin-sidebar .markdown-task').click()");
        await Until(async () => await Js("pendingTask === null") == "true");
        Check(await Js("Array.from(document.querySelectorAll('.markdown-task')).every(t=>!t.checked)") == "true", "Pin checkbox syncs all copies and the main document");
        await Until(async () => await other.ExecuteScriptAsync("document.querySelector('#markdown-main .markdown-task').checked") == "false");
        Check(before == await Js("JSON.stringify([scrollY,document.getElementById('pin-sidebar').scrollTop])"), "Checkbox saving does not move main or sidebar scroll");
        Check(File.ReadAllText(path).Contains("[ ] Saved task"), "Pin checkbox writes the original source file");

        await Js("scrollTo(0,1800);document.querySelector('.pin-content').click()");
        Check(await Js("scrollY") == "1800", "Clicking pin content does not jump");
        await Js("document.querySelector('.pin-header').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}))");
        Check(await Js("scrollY") != "1800", "Double-clicking only the header jumps to the passage");
        await Js("document.getElementById('pin-toggle').click()");
        Check(await Js("CSS.highlights.has('markdown-pins')") == "false", "Closing the sidebar removes pin highlighting");

        await SelectWithMouse(web, "#section-30 + p strong [data-run]", 0, 9);
        await Js("document.getElementById('section-30').scrollIntoView()");
        await BeginPointerDrag(web);
        await DragPointer(web, "dragEnter", 10, 160);
        await DragPointer(web, "dragOver", 10, 160);
        await Task.Delay(500);
        Check(await Js("sidebarOpen") == "false", "Edge hover waits before revealing pins");
        await DragPointer(web, "dragOver", 200, 160);
        await Task.Delay(600);
        Check(await Js("sidebarOpen") == "false", "Leaving the edge cancels delayed reveal");
        var leftBefore = await Js("document.getElementById('markdown-main').getBoundingClientRect().left");
        await DragPointer(web, "dragOver", 10, 160);
        await Task.Delay(1100);
        Check(await Js("document.body.classList.contains('pins-overlay')") == "true", "Pointer drag reveals the sidebar as an overlay");
        Check(await Js("getComputedStyle(pinDivider).display") == "\"none\"", "Overlay reveal never exposes the resize divider");
        Check(leftBefore == await Js("document.getElementById('markdown-main').getBoundingClientRect().left"), "Drag reveal does not move selected content");
        await DragPointer(web, "dragCancel", 10, 160);
        await Mouse(web, "mouseReleased", 10, 160, 0);
        await Until(async () => await Js("sidebarOpen") == "false");
        Check(await Count() == 3, "Cancelled pointer drag restores closed sidebar without creating a pin");

        await SelectWithMouse(web, "#section-30 + p strong [data-run]", 0, 9);
        await BeginPointerDrag(web);
        await DragPointer(web, "dragEnter", 10, 160);
        await DragPointer(web, "dragOver", 10, 160);
        await Task.Delay(1100);
        var beforeDropAnchor = JsonSerializer.Deserialize<JsonElement>(await Js("captureMarkdownAnchor()")).GetProperty("line").GetDouble();
        await DragPointer(web, "drop", 100, 160);
        await Mouse(web, "mouseReleased", 100, 160, 0);
        await Until(async () => await Count() == 4, async () => await Js("document.getElementById('pin-status').textContent"));
        Check(await Js("sidebarOpen && !document.body.classList.contains('pins-overlay')") == "true", "Successful pointer drag pins text and settles the sidebar layout");
        await Task.Delay(100);
        Check(Math.Abs(JsonSerializer.Deserialize<JsonElement>(await Js("captureMarkdownAnchor()")).GetProperty("line").GetDouble() - beforeDropAnchor) < 1,
            "Settling the sidebar preserves the reading passage");
        Check(!web.AllowExternalDrop && !web.AllowDrop, "Expanded external file drops remain disabled");
        foreach (var selection in new[] { ("#markdown-main li [data-run]", 5), ("#markdown-main [data-run]:has(pre code)", 6) })
        {
            await Js($"document.querySelector({JsonSerializer.Serialize(selection.Item1)}).scrollIntoView({{block:'center'}})");
            await SelectWithMouse(web, selection.Item1, 0, selection.Item2);
            var count = await Count();
            await BeginPointerDrag(web);
            await DragPointer(web, "dragEnter", 100, 200);
            await DragPointer(web, "dragOver", 100, 200);
            await DragPointer(web, "drop", 100, 200);
            await Mouse(web, "mouseReleased", 100, 200, 0);
            await Until(async () => await Count() == count+1, async () => await Js("document.getElementById('pin-status').textContent"));
        }
        Check(await Js("Array.from(pinList.children).find(c=>c.dataset.pinId===pins.at(-1).id).querySelector('pre code').textContent") == "\"SELECT\"", "Pointer code dragging preserves the exact partial code excerpt");
        var disk = File.ReadAllText(path);
        var beforeUnpin = await Count();
        await Js("document.querySelector('.pin-unpin').click()");
        await Until(async () => await Count() == beforeUnpin - 1);
        Check(File.ReadAllText(path) == disk, "Unpin changes only metadata");
        await Js("{const a=document.querySelector('#section-20 [data-run]').firstChild;const b=document.querySelectorAll('#markdown-main [data-run]')[120].firstChild; const r=document.createRange();r.setStart(a,2);r.setEnd(b,Math.min(5,b.length)); getSelection().removeAllRanges();getSelection().addRange(r);}");
        // Use a known heading-to-code range spanning multiple formatted blocks.
        await Js("{const h=document.getElementById('section-20');const a=h.querySelector('[data-run]').firstChild;let e=h.nextElementSibling;while(e&&!e.querySelector('pre code'))e=e.nextElementSibling;const b=e.querySelector('pre code').firstChild;const r=document.createRange();r.setStart(a,2);r.setEnd(b,12);getSelection().removeAllRanges();getSelection().addRange(r);}");
        await Task.Delay(50);
        await Pin();
        Check(await Js("!!Array.from(pinList.children).find(c=>c.dataset.pinId===pins.at(-1).id).querySelector('h2') && !!Array.from(pinList.children).find(c=>c.dataset.pinId===pins.at(-1).id).querySelector('strong') && !!Array.from(pinList.children).find(c=>c.dataset.pinId===pins.at(-1).id).querySelector('pre code')") == "true",
            "A partial multi-block selection keeps heading, emphasis, list and code formatting");
        await Js("document.getElementById('section-20').scrollIntoView();document.getElementById('pin-sidebar').scrollTop=0");
        Directory.CreateDirectory("artifacts/MarkdownPopoutSmoke");
        using (var image = File.Create("artifacts/MarkdownPopoutSmoke/pins-preview.png"))
            await web.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
        Check(await Js("Array.from(document.querySelectorAll('#markdown-main .code-block')).every(block=>!!block.querySelector('.copy-code'))") == "true", "Pin cloning preserves every main-document code-copy button");
        await Js("document.getElementById('pin-sidebar').scrollTop=99999;scrollTo(0,1600)");
        var sidebarScroll = await Js("document.getElementById('pin-sidebar').scrollTop");
        Check(double.Parse(sidebarScroll, System.Globalization.CultureInfo.InvariantCulture) > 0, "Pin sidebar scrolls independently");
        await Js("scrollTo(0,1700)");
        Check(sidebarScroll == await Js("document.getElementById('pin-sidebar').scrollTop"), "Main scrolling leaves sidebar scroll unchanged");
        await PinLayoutChecks.Run(web, other, path);

        var session = MarkdownDocumentSession.Get(path);
        var tracked = session.Pins.First();
        var editor = (TextEditor)preview.FindName("MarkdownSourceEditor");
        var toggle = (Button)preview.FindName("EditMarkdownButton");
        async Task Toggle(bool source)
        {
            var oldId = await Js("documentId");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(async () => toggle.IsEnabled && editor.IsVisible == source && (source || await Js("documentId") != oldId));
        }
        await Toggle(true);
        Check(!web.IsVisible, "Source mode temporarily hides rendered pins");
        const string inserted = "Inserted above all pins.\n\n";
        editor.Document.Insert(0, inserted);
        await Toggle(false);
        Check(await Js("sidebarOpen") == "true", "Preview restores prior sidebar visibility");
        Check(await Js("document.getElementById('pin-selection').disabled") == "true", "Unsaved drafts clearly disable creating persistent pins");
        var provisional = JsonSerializer.Deserialize<JsonElement>(await Js($"pins.find(p=>p.id==={JsonSerializer.Serialize(tracked.Id)})"));
        Check(Math.Abs(provisional.GetProperty("start").GetDouble() - tracked.Start - inserted.Length) < .01, "Draft pin range follows precise source edits");
        Check(new MarkdownPinStore().Load(path).Pins.First(p=>p.Id==tracked.Id).Start == tracked.Start, "Draft movement does not commit pin metadata");
        ((Button)preview.FindName("SaveMarkdownButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Task.FromResult(toggle.IsEnabled && File.ReadAllText(path) == editor.Text));
        Check(new MarkdownPinStore().Load(path).Pins.First(p=>p.Id==tracked.Id).Start == tracked.Start + inserted.Length, "Save commits updated live pin anchors");
        await Toggle(true);
        var current = session.Pins.First(p=>p.Id==tracked.Id);
        editor.Document.Remove((int)Math.Floor(current.Start), (int)Math.Ceiling(current.End)-(int)Math.Floor(current.Start));
        await Toggle(false);
        Check(await Js($"!!document.querySelector('[data-pin-id=\"{tracked.Id}\"] .pin-unavailable')") == "true", "Deleting a draft passage makes the pin unavailable rather than jumping elsewhere");
        await Program.AnswerDialog(6, () => ((Button)preview.FindName("DiscardMarkdownButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), "Discard changes");
        await Until(async () => await Js($"!!document.querySelector('[data-pin-id=\"{tracked.Id}\"] .pin-content')") == "true");
        Check(true, "Discard restores saved pin anchors and content");
        var saved = File.ReadAllText(path);
        File.WriteAllText(path, saved + "\n" + saved);
        session.Refresh();
        await Until(async () => await Js("pins.every(p=>!!p.unavailable)") == "true");
        Check(await Js("document.querySelectorAll('#pin-list input').length") == "0", "Ambiguous external passages disable pin jumps and writes");
        File.Delete(path); session.Refresh();
        await Until(async () => await Js("document.getElementById('markdown-main').textContent.includes('Source unavailable')") == "true");
        Check(await Js("document.querySelectorAll('.pin-unavailable').length") == (await Count()).ToString(), "Missing source retains identifiable unavailable pins");
        File.WriteAllText(path, saved); session.Refresh();
        await Until(async () => await Js("pins.every(p=>!p.unavailable)") == "true");
        Check(true, "Restored exact source safely resolves the saved pins");
        // Leave the previous editing smoke's document-wide code/checkbox assertions unchanged.
        foreach (var pin in MarkdownDocumentSession.Get(path).Pins) MarkdownDocumentSession.Get(path).Unpin(pin.Id);
        await Until(async () => await Count() == 0);
        await Js("if(sidebarOpen)document.getElementById('pin-toggle').click()");
        File.WriteAllText(path, originalSource); session.Refresh();
        await Until(async () => toggle.IsEnabled && await Js("!document.getElementById('markdown-main').textContent.includes('Inserted above all pins.') && document.querySelector('#markdown-main h1')?.textContent === 'Rendered heading'") == "true");
        Console.WriteLine("PASS: formatted persistent pins, shared checkbox state and uninterrupted pointer drag/reveal/cancel");
        await PinManagementChecks.Run(preview, web, other, path);
    }

    private static async Task SelectWithMouse(WebView2 web, string selector, int start, int length)
    {
        await web.ExecuteScriptAsync($"document.querySelector({JsonSerializer.Serialize(selector)}).scrollIntoView({{block:'center'}})");
        await Task.Delay(60);
        // Clear an earlier selection with an ordinary click outside the text.
        await Mouse(web, "mousePressed", 700, 20, 1); await Mouse(web, "mouseReleased", 700, 20, 0);
        var points = JsonSerializer.Deserialize<JsonElement>(await web.ExecuteScriptAsync($"{{const n=mappedNodes(document.querySelector({JsonSerializer.Serialize(selector)}))[0];const r=document.createRange();r.setStart(n,{start});r.setEnd(n,{start+length});const a=r.getClientRects()[0],b=Array.from(r.getClientRects()).at(-1);({{x:a.left+.1,y:a.top+a.height/2,ex:b.right-.1,ey:b.top+b.height/2}})}}"));
        double x=points.GetProperty("x").GetDouble(), y=points.GetProperty("y").GetDouble();
        await Mouse(web,"mousePressed",x,y,1);
        await Mouse(web,"mouseMoved",points.GetProperty("ex").GetDouble(),points.GetProperty("ey").GetDouble(),1);
        await Mouse(web,"mouseReleased",points.GetProperty("ex").GetDouble(),points.GetProperty("ey").GetDouble(),0);
        await Task.Delay(60);
        Check(await web.ExecuteScriptAsync("!!lastPinSelection") == "true", "Mouse selection produces a pinnable source range");
    }
    private static async Task BeginPointerDrag(WebView2 web)
    {
        // No drag interception, injected drag payload, dispatchDragEvent or DOM drag events.
        var point = JsonSerializer.Deserialize<JsonElement>(await web.ExecuteScriptAsync("{const r=getSelection().getRangeAt(0).getClientRects()[0];({x:r.x+Math.min(5,r.width/2),y:r.y+r.height/2})}"));
        double x=point.GetProperty("x").GetDouble(), y=point.GetProperty("y").GetDouble();
        await Mouse(web,"mousePressed",x,y,1);
        await Mouse(web,"mouseMoved",x-15,y,1);
        Check(await web.ExecuteScriptAsync("!!activePinDrag") == "true", "Second mouse-down and movement start internal drag without losing selection");
    }
    internal static Task<string> Mouse(WebView2 web, string type, double x, double y, int buttons) =>
        web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new{type,x,y,button="left",buttons,clickCount=type=="mouseMoved"?0:1}));
    private static async Task<string> DragPointer(WebView2 web,string type,double x,double y)
    {
        if (type == "dragCancel")
            return await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", "{\"type\":\"keyDown\",\"key\":\"Escape\",\"code\":\"Escape\",\"windowsVirtualKeyCode\":27}");
        await Mouse(web,"mouseMoved",x,y,1);
        return type == "drop" ? await Mouse(web,"mouseReleased",x,y,0) : "";
    }
    private static async Task Until(Func<Task<bool>> condition, Func<Task<string>>? diagnostic=null)
    {
        var deadline=DateTime.UtcNow.AddSeconds(15);
        while(DateTime.UtcNow<deadline){if(await condition())return;await Task.Delay(100);}
        throw new Exception("Pin check timed out. " + (diagnostic is null ? "" : await diagnostic()));
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
