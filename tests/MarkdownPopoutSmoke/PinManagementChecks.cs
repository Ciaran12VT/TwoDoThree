using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using Microsoft.Web.WebView2.Wpf;
using TwoDoThree.Controls;
using TwoDoThree.Services;

internal static class PinManagementChecks
{
    public static async Task Run(FileResourcePreviewControl preview, WebView2 web, WebView2 other, string path)
    {
        async Task<string> Js(string s) => await web.ExecuteScriptAsync(s);
        var original=File.ReadAllText(path);
        var session=MarkdownDocumentSession.Get(path);
        string Add(string text)
        {
            var start=session.Text!.IndexOf(text,StringComparison.Ordinal);
            session.AddPin(session.Text!,start,start+text.Length,text);
            return session.Pins.Last().Id;
        }
        string Card(string id)=>$"pinList.querySelector('[data-pin-id=\"{id}\"]')";
        var late=Add("formatted passage 60");
        var early=Add("formatted passage 10");
        var shorter=Add("formatted passage 1"); // First match is section 1, before both.
        var overlapStart=session.Text!.IndexOf("formatted passage 10",StringComparison.Ordinal);
        session.AddPin(session.Text!,overlapStart,overlapStart+9,"formatted");
        var overlap=session.Pins.Last().Id;
        var expected=new[]{shorter,overlap,early,late};
        const string order="Array.from(pinList.children,c=>c.dataset.pinId)";
        await Until(async()=>await Js(order)==JsonSerializer.Serialize(expected));
        await Until(async()=>await other.ExecuteScriptAsync(order)==JsonSerializer.Serialize(expected));
        Check(true,"Pins added out of order follow source start/end with stable overlap ordering in both windows");
        await Js("if(!sidebarOpen)pinToggle.click();pinSidebar.scrollTop=0;scrollTo(0,1400)");
        var scroll=await Js("scrollY");
        await Js(Card(late)+".querySelector('.pin-collapse').click()");
        await Until(async()=>await Js(Card(late)+".lastElementChild.hidden")=="true");
        Check(await Js(Card(late)+".querySelector('.pin-collapse').getAttribute('aria-expanded')")=="\"false\"","Collapse exposes its accessible expanded state");
        Check(scroll==await Js("scrollY"),"Collapsing a pin never navigates the main document");
        Check(new MarkdownDocumentSession(path,new MarkdownPinStore()).Pins.Single(p=>p.Id==late).Collapsed,"Collapsed state persists in per-file metadata");
        await Until(async()=>await other.ExecuteScriptAsync(Card(late)+".lastElementChild.hidden")=="true");
        await Js(Card(late)+".querySelector('.pin-collapse').click()");
        await Until(async()=>await Js(Card(late)+".lastElementChild.hidden")=="false");
        Check(true,"Expand restores the live excerpt and syncs across views");

        async Task OpenRename(string id)
        {
            await Js(Card(id)+".scrollIntoView({block:'center'})");
            var point=JsonSerializer.Deserialize<JsonElement>(await Js("{const r="+Card(id)+".querySelector('.pin-header').getBoundingClientRect();({x:r.left+40,y:r.top+12})}"));
            var x=point.GetProperty("x").GetDouble(); var y=point.GetProperty("y").GetDouble();
            foreach(var type in new[]{"mousePressed","mouseReleased"})
                await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type,x,y,button="right",buttons=type=="mousePressed"?2:0,clickCount=1}));
            await Until(async()=>await Js("!cardMenu.hidden")=="true");
            await Js("renameButton.click()");
            Check(await Js("renameDialog.open")=="true","Right-click card menu opens the Rename dialog");
        }
        var titleBefore=session.Pins.Single(p=>p.Id==late).Title;
        await OpenRename(late);
        await Js("titleInput.value='Cancelled title';document.getElementById('pin-rename-cancel').click()");
        Check(session.Pins.Single(p=>p.Id==late).Title==titleBefore,"Cancel rename leaves the title unchanged");
        await OpenRename(late);
        await Js("titleInput.value='   ';document.getElementById('pin-rename-form').requestSubmit()");
        Check(await Js("renameDialog.open && !!document.getElementById('pin-title-error').textContent")=="true","Rename rejects whitespace-only titles without changing metadata");
        const string title="<b>Review & follow-up</b>";
        await Js("titleInput.value="+JsonSerializer.Serialize(title)+";document.getElementById('pin-rename-form').requestSubmit()");
        await Until(async()=>JsonSerializer.Deserialize<string>(await Js(Card(late)+".querySelector('.pin-title').textContent"))==title);
        Check(await Js(Card(late)+".querySelectorAll('.pin-title b').length")=="0","Custom title is rendered as text, never HTML");
        await Until(async()=>JsonSerializer.Deserialize<string>(await other.ExecuteScriptAsync(Card(late)+".querySelector('.pin-title').textContent"))==title);
        Check(File.ReadAllText(path)==original,"Collapse and rename preserve source bytes");
        session.SetPinCollapsed(late,true);

        // Move an intact section externally so conservative source matching can
        // establish its new position, then save an inline edit and reload.
        var start=original.IndexOf("## Section 60\n",StringComparison.Ordinal);
        var end=original.IndexOf("## Section 61\n",StringComparison.Ordinal);
        var moved=original[start..end]+original[..start]+original[end..];
        File.WriteAllText(path,moved); session.Refresh();
        expected=[late,shorter,overlap,early];
        await Until(async()=>await Js(order)==JsonSerializer.Serialize(expected));
        await Until(async()=>await other.ExecuteScriptAsync(order)==JsonSerializer.Serialize(expected));
        Check(await Js(Card(late)+".lastElementChild.hidden")=="true" && JsonSerializer.Deserialize<string>(await Js(Card(late)+".querySelector('.pin-title').textContent"))==title,
            "Source reordering preserves custom title and collapsed state");
        var editor=(TextEditor)preview.FindName("MarkdownSourceEditor");
        var toggle=(Button)preview.FindName("EditMarkdownButton");
        async Task Toggle(bool source)
        {
            var oldId=await Js("documentId");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(async()=>toggle.IsEnabled && editor.IsVisible==source && (source || await Js("documentId")!=oldId));
        }
        await Toggle(true); editor.Document.Insert(0,"A saved introduction.\n\n");
        ((Button)preview.FindName("SaveMarkdownButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(()=>Task.FromResult(toggle.IsEnabled && File.ReadAllText(path)==editor.Text));
        await Toggle(false);
        await Until(async()=>await Js(order)==JsonSerializer.Serialize(expected));
        var reloaded=new MarkdownDocumentSession(path,new MarkdownPinStore());
        Check(reloaded.Pins.Single(p=>p.Id==late).Title==title && reloaded.Pins.Single(p=>p.Id==late).Collapsed,
            "Reordered pins retain title and collapse after source Save and metadata reload");
        Check(true,"Document order updates after a source move and survives inline Save/reload");
        await Js("pinSidebar.scrollTop=0");
        Directory.CreateDirectory("artifacts/MarkdownPopoutSmoke");
        using(var screenshot=File.Create("artifacts/MarkdownPopoutSmoke/pins-managed.png"))
            await web.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,screenshot);

        // Make only the later section-10 passages ambiguous. They must sort after
        // the available section-60/section-1 pins, with stable prior positions.
        var current=File.ReadAllText(path);
        var ten=current.IndexOf("## Section 10\n",StringComparison.Ordinal);
        var eleven=current.IndexOf("## Section 11\n",StringComparison.Ordinal);
        File.WriteAllText(path,current+"\n"+current[Math.Max(0,ten-100)..eleven]); session.Refresh();
        await Until(async()=>await Js("pins.some(p=>!!p.unavailable)")=="true");
        Check(await Js("Array.from(pinList.children).slice(-2).every(c=>!!c.querySelector('.pin-unavailable'))")=="true",
            "Unavailable pins sort stably after located passages without inventing source matches");
        var clearBytes=File.ReadAllBytes(path);
        var preferences=(session.SidebarWidth,session.Wrap);
        Check(await Js("clearPinsButton.textContent")=="\"Clear all pins\"","Clear all pins is a visible text control above the cards");
        await Program.AnswerDialog(2,()=>_ = Js("clearPinsButton.click()"),"Clear all pins");
        Check(session.Pins.Count==4 && File.ReadAllBytes(path).SequenceEqual(clearBytes),"Cancelling Clear all pins changes nothing");
        await Program.AnswerDialog(1,()=>_ = Js("clearPinsButton.click()"),"Clear all pins");
        await Until(async()=>await Js("pins.length")=="0" && await other.ExecuteScriptAsync("pins.length")=="0");
        Check(File.ReadAllBytes(path).SequenceEqual(clearBytes),"Confirmed clear removes collapsed and unavailable pins without changing source bytes");
        Check((session.SidebarWidth,session.Wrap)==preferences,"Clear preserves document sidebar width and wrapping preferences");
        Check(await Js("clearPinsButton.disabled && (!CSS.highlights.get('markdown-pins') || CSS.highlights.get('markdown-pins').size===0)")=="true",
            "Clearing removes highlights, synchronizes windows and disables the empty clear control");
        var previousDocumentId=await Js("documentId");
        File.WriteAllText(path,original); session.Refresh();
        await Until(async()=>toggle.IsEnabled && await Js("documentId")!=previousDocumentId
            && await Js("document.querySelector('#markdown-main h1')?.textContent")=="\"Rendered heading\"");
        await Js("if(sidebarOpen)pinToggle.click()");
    }
    private static async Task Until(Func<Task<bool>> condition)
    {
        var end=DateTime.UtcNow.AddSeconds(20);
        while(DateTime.UtcNow<end){if(await condition())return;await Task.Delay(70);}
        throw new Exception("Pin management check timed out.");
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
