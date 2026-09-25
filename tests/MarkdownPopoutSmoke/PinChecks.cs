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
            await Js("document.getElementById('pin-selection').click()");
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

        await Select("#section-30 + p strong [data-run]", 0, 9);
        await Js("document.getElementById('section-30').scrollIntoView()");
        var data = await BeginNativeDrag(web);
        await Drag(web, "dragEnter", data, 10, 160);
        await Drag(web, "dragOver", data, 10, 160);
        await Task.Delay(500);
        Check(await Js("sidebarOpen") == "false", "Edge hover waits before revealing pins");
        await Drag(web, "dragOver", data, 200, 160);
        await Task.Delay(600);
        Check(await Js("sidebarOpen") == "false", "Leaving the edge cancels delayed reveal");
        var leftBefore = await Js("document.getElementById('markdown-main').getBoundingClientRect().left");
        await Drag(web, "dragOver", data, 10, 160);
        await Task.Delay(1100);
        Check(await Js("document.body.classList.contains('pins-overlay')") == "true", "Native drag reveals the sidebar as an overlay");
        Check(leftBefore == await Js("document.getElementById('markdown-main').getBoundingClientRect().left"), "Drag reveal does not move selected content");
        await Drag(web, "dragCancel", data, 10, 160);
        await Mouse(web, "mouseReleased", 10, 160, 0);
        await Until(async () => await Js("sidebarOpen") == "false");
        Check(await Count() == 3, "Cancelled native drag restores closed sidebar without creating a pin");

        await Select("#section-30 + p strong [data-run]", 0, 9);
        data = await BeginNativeDrag(web);
        await Drag(web, "dragEnter", data, 10, 160);
        await Drag(web, "dragOver", data, 10, 160);
        await Task.Delay(1100);
        var beforeDropAnchor = JsonSerializer.Deserialize<JsonElement>(await Js("captureMarkdownAnchor()")).GetProperty("line").GetDouble();
        await Drag(web, "drop", data, 100, 160);
        await Mouse(web, "mouseReleased", 100, 160, 0);
        await Until(async () => await Count() == 4, async () => await Js("document.getElementById('pin-status').textContent"));
        Check(await Js("sidebarOpen && !document.body.classList.contains('pins-overlay')") == "true", "Successful native drag pins text and settles the sidebar layout");
        await Task.Delay(100);
        Check(Math.Abs(JsonSerializer.Deserialize<JsonElement>(await Js("captureMarkdownAnchor()")).GetProperty("line").GetDouble() - beforeDropAnchor) < 1,
            "Settling the sidebar preserves the reading passage");
        Check(!web.AllowExternalDrop && !web.AllowDrop, "Expanded external file drops remain disabled");
        foreach (var selection in new[] { ("#markdown-main li [data-run]", 5), ("#markdown-main [data-run]:has(pre code)", 6) })
        {
            await Js($"document.querySelector({JsonSerializer.Serialize(selection.Item1)}).scrollIntoView({{block:'center'}})");
            await Select(selection.Item1, 0, selection.Item2);
            var count = await Count();
            data = await BeginNativeDrag(web);
            await Drag(web, "dragEnter", data, 100, 200);
            await Drag(web, "dragOver", data, 100, 200);
            await Drag(web, "drop", data, 100, 200);
            await Mouse(web, "mouseReleased", 100, 200, 0);
            await Until(async () => await Count() == count+1, async () => await Js("document.getElementById('pin-status').textContent"));
        }
        Check(await Js("document.querySelector('.pin-card:last-child pre code').textContent") == "\"SELECT\"", "Native code dragging preserves the exact partial code excerpt");
        var disk = File.ReadAllText(path);
        var beforeUnpin = await Count();
        await Js("document.querySelector('.pin-header button').click()");
        await Until(async () => await Count() == beforeUnpin - 1);
        Check(File.ReadAllText(path) == disk, "Unpin changes only metadata");
        await Js("{const a=document.querySelector('#section-20 [data-run]').firstChild;const b=document.querySelectorAll('#markdown-main [data-run]')[120].firstChild; const r=document.createRange();r.setStart(a,2);r.setEnd(b,Math.min(5,b.length)); getSelection().removeAllRanges();getSelection().addRange(r);}");
        // Use a known heading-to-code range spanning multiple formatted blocks.
        await Js("{const h=document.getElementById('section-20');const a=h.querySelector('[data-run]').firstChild;let e=h.nextElementSibling;while(e&&!e.querySelector('pre code'))e=e.nextElementSibling;const b=e.querySelector('pre code').firstChild;const r=document.createRange();r.setStart(a,2);r.setEnd(b,12);getSelection().removeAllRanges();getSelection().addRange(r);}");
        await Task.Delay(50);
        await Pin();
        Check(await Js("!!document.querySelector('.pin-card:last-child h2') && !!document.querySelector('.pin-card:last-child strong') && !!document.querySelector('.pin-card:last-child pre code')") == "true",
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
        Check(await Js("document.querySelectorAll('#pin-sidebar input').length") == "0", "Ambiguous external passages disable pin jumps and writes");
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
        Console.WriteLine("PASS: formatted persistent pins, shared checkbox state and native browser drag/reveal/cancel");
    }

    private static async Task<JsonElement> BeginNativeDrag(WebView2 web)
    {
        var core = web.CoreWebView2;
        var captured = new TaskCompletionSource<string>();
        var receiver = core.GetDevToolsProtocolEventReceiver("Input.dragIntercepted");
        void OnDrag(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2DevToolsProtocolEventReceivedEventArgs e) => captured.TrySetResult(e.ParameterObjectAsJson);
        receiver.DevToolsProtocolEventReceived += OnDrag;
        try
        {
            await core.CallDevToolsProtocolMethodAsync("Input.setInterceptDrags", "{\"enabled\":true}");
            var point = JsonSerializer.Deserialize<JsonElement>(await web.ExecuteScriptAsync("{const r=getSelection().getRangeAt(0).getBoundingClientRect();({x:r.x+5,y:r.y+8})}"));
            double x=point.GetProperty("x").GetDouble(), y=point.GetProperty("y").GetDouble();
            await Mouse(web, "mousePressed", x, y, 1);
            await Mouse(web, "mouseMoved", x-25, y, 1);
            await Mouse(web, "mouseMoved", 10, y, 1);
            return JsonDocument.Parse(await captured.Task.WaitAsync(TimeSpan.FromSeconds(5))).RootElement.GetProperty("data").Clone();
        }
        finally { receiver.DevToolsProtocolEventReceived -= OnDrag; }
    }
    private static Task<string> Mouse(WebView2 web, string type, double x, double y, int buttons) =>
        web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new{type,x,y,button="left",buttons,clickCount=type=="mouseMoved"?0:1}));
    private static Task<string> Drag(WebView2 web,string type,JsonElement data,double x,double y) =>
        web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchDragEvent",JsonSerializer.Serialize(new{type,x,y,data}));
    private static async Task Until(Func<Task<bool>> condition, Func<Task<string>>? diagnostic=null)
    {
        var deadline=DateTime.UtcNow.AddSeconds(15);
        while(DateTime.UtcNow<deadline){if(await condition())return;await Task.Delay(100);}
        throw new Exception("Pin check timed out. " + (diagnostic is null ? "" : await diagnostic()));
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
