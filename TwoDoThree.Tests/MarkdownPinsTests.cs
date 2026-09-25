using System.Text;
using TwoDoThree.Models;
using TwoDoThree.Services;

namespace TwoDoThree.Tests;

public class MarkdownPinsTests
{
    [Fact]
    public void PinNamesCollapseAndClearAreMetadataOnlyAndPreservePreferences()
    {
        WithFile("A pinned passage here", (path,store) =>
        {
            var session=new MarkdownDocumentSession(path,store);
            session.AddPin(session.Text!,2,16,"pinned passage");
            var pin=session.Pins.Single();
            var bytes=File.ReadAllBytes(path);
            session.SetViewPreferences(430,false);
            session.RenamePin(pin.Id,"  <b>Review & check</b>  ");
            session.SetPinCollapsed(pin.Id,true);
            var saved=new MarkdownDocumentSession(path,store).Pins.Single();
            Assert.Equal("<b>Review & check</b>",saved.Title); Assert.True(saved.Collapsed);
            Assert.Throws<ArgumentException>(()=>session.RenamePin(pin.Id,"  "));
            Assert.Throws<ArgumentException>(()=>session.RenamePin(pin.Id,"one\ntwo"));
            Assert.Throws<ArgumentException>(()=>session.RenamePin(pin.Id,new string('x',201)));
            Assert.Throws<IOException>(()=>session.RenamePin(Guid.NewGuid().ToString("N"),"missing"));
            session.ClearPins();
            var empty=new MarkdownDocumentSession(path,store);
            Assert.Empty(empty.Pins); Assert.Equal(430,empty.SidebarWidth); Assert.False(empty.Wrap);
            Assert.Equal(bytes,File.ReadAllBytes(path));
        });
    }

    [Fact]
    public void DraftSaveKeepsConcurrentPinMetadataChangesAndDoesNotResurrectClearedPins()
    {
        WithFile("A pinned passage here", (path,store) =>
        {
            var session=new MarkdownDocumentSession(path,store);
            session.AddPin(session.Text!,2,16,"pinned passage");
            var original=session.Text!;
            var draft="new " + original;
            var stale=MarkdownPinAnchors.ApplyEdit(session.Pins.Single(),0,0,4,draft);
            session.RenamePin(stale.Id,"Updated elsewhere"); session.SetPinCollapsed(stale.Id,true);
            session.SaveText(original,draft,[stale]);
            Assert.Equal("Updated elsewhere",session.Pins.Single().Title);
            Assert.True(session.Pins.Single().Collapsed); Assert.Equal(6,session.Pins.Single().Start);
            session.ClearPins(); session.SaveText(draft,draft+" edited",[stale]);
            Assert.Empty(session.Pins);
        });
    }

    [Fact]
    public void MetadataChangesDoNotReviveAmbiguousPinsAndClearIsDocumentScoped()
    {
        WithFile("prefix passage suffix", (path,store) =>
        {
            var session=new MarkdownDocumentSession(path,store);
            session.AddPin(session.Text!,7,14,"passage");
            var id=session.Pins.Single().Id;
            File.AppendAllText(path,"\nprefix passage suffix"); session.Refresh();
            Assert.NotNull(session.Pins.Single().Unavailable);
            session.RenamePin(id,"Unavailable item"); session.SetPinCollapsed(id,true);
            Assert.NotNull(new MarkdownDocumentSession(path,store).Pins.Single().Unavailable);
            var ambiguous=File.ReadAllText(path);
            File.Delete(path); session.Refresh(); session.RenamePin(id,"Still unavailable");
            File.WriteAllText(path,ambiguous); session.Refresh();
            Assert.NotNull(new MarkdownDocumentSession(path,store).Pins.Single().Unavailable);
            var otherPath=path+".md";
            try
            {
                File.WriteAllText(otherPath,"Other passage");
                var other=new MarkdownDocumentSession(otherPath,store);
                other.AddPin(other.Text!,0,5,"Other");
                session.ClearPins();
                Assert.Empty(new MarkdownDocumentSession(path,store).Pins);
                Assert.Single(new MarkdownDocumentSession(otherPath,store).Pins);
            }
            finally { File.Delete(otherPath); }
        });
    }
    [Fact]
    public void ViewPreferencesPersistWithoutChangingSourceOrAnchors()
    {
        WithFile("Some text", (path,store) =>
        {
            var session=new MarkdownDocumentSession(path,store);
            Assert.Equal(330,session.SidebarWidth); Assert.True(session.Wrap);
            session.AddPin(session.Text!,0,4,"Some");
            var hash=store.Load(path).SourceHash;
            var bytes=File.ReadAllBytes(path);
            session.SetViewPreferences(420,false);
            var reloaded=new MarkdownDocumentSession(path,store);
            Assert.Equal(420,reloaded.SidebarWidth); Assert.False(reloaded.Wrap);
            Assert.Equal(hash,store.Load(path).SourceHash);
            Assert.Equal(bytes,File.ReadAllBytes(path));
            session.Unpin(session.Pins.Single().Id);
            Assert.Equal(420,store.Load(path).SidebarWidth);
            Assert.Throws<ArgumentException>(()=>session.SetViewPreferences(double.NaN,true));
            Assert.Throws<ArgumentException>(()=>session.SetViewPreferences(10,true));
        });
    }
    [Theory]
    [InlineData("A **formatted passage** follows", "matted pass")]
    [InlineData("```sql\nselect 'value';\n```", "select 'val")]
    [InlineData("A &amp; B", "&")]
    [InlineData("An `inline code` example", "line co")]
    [InlineData("A &NotEqualTilde; B", "̸")]
    [InlineData("    indented code", "dented co")]
    [InlineData("<div>literal markup</div>", "literal")]
    public void CharacterBoundariesValidatePartialSelections(string source, string selection)
    {
        var run = MarkdownSelectionMap.GetRuns(source).Single(r => r.Text.Contains(selection));
        int start = run.Text.IndexOf(selection, StringComparison.Ordinal);
        MarkdownSelectionMap.Validate(source, run.Boundaries[start], run.Boundaries[start + selection.Length], selection);
        Assert.Throws<ArgumentException>(() => MarkdownSelectionMap.Validate(source, run.Boundaries[start], run.Boundaries[start + selection.Length], "different text"));
        if (source.StartsWith("```")) Assert.True(run.Boundaries[0] >= source.IndexOf('\n') + 1);
    }

    [Fact]
    public void ExactEditsMoveAnchorsAndDeletionDoesNotJumpToADuplicate()
    {
        var source = "before unique passage after";
        var pin = MarkdownPinAnchors.Capture(new() { Start = 7, End = 21 }, source);
        var moved = MarkdownPinAnchors.ApplyEdit(pin, 0, 0, 4, "new " + source);
        Assert.Equal(11, moved.Start);
        var updated = MarkdownPinAnchors.ApplyEdit(moved, 17, 0, 4, "new before unique NEW passage after");
        Assert.Contains("NEW", updated.Quote);
        var deleted = MarkdownPinAnchors.ApplyEdit(pin, 7, 14, 0, "before  after unique passage");
        Assert.NotNull(deleted.Unavailable);
        Assert.NotNull(MarkdownPinAnchors.Resolve(deleted, source, false).Unavailable);
        var replaced = MarkdownPinAnchors.ApplyEdit(pin, 7, 14, 3, "before new after");
        Assert.Null(replaced.Unavailable);
        Assert.Equal("new", replaced.Quote);
    }

    [Fact]
    public void ExternalRecoveryUsesContextAndRefusesAmbiguousMatches()
    {
        var source = "prefix passage suffix";
        var pin = MarkdownPinAnchors.Capture(new() { Start = 7, End = 14 }, source);
        Assert.Equal(13, MarkdownPinAnchors.Resolve(pin, "moved " + source, false).Start);
        Assert.NotNull(MarkdownPinAnchors.Resolve(pin, source + "\n" + source, false).Unavailable);
        Assert.NotNull(MarkdownPinAnchors.Resolve(pin, null, false).Unavailable);
    }

    [Fact]
    public void MetadataRoundTripsPerFileAndUnpinNeverChangesSource()
    {
        WithFile("Some **formatted text** here", (path, store) =>
        {
            var session = new MarkdownDocumentSession(path, store);
            session.AddPin(session.Text!, 7, 16, "formatted");
            var pin = Assert.Single(new MarkdownDocumentSession(path, store).Pins);
            Assert.Equal("formatted", pin.SelectedText);
            Assert.Equal(pin.Id, Assert.Single(store.Load(path).Pins).Id);
            session.Unpin(pin.Id);
            Assert.Empty(new MarkdownDocumentSession(path, store).Pins);
            Assert.Equal("Some **formatted text** here", File.ReadAllText(path));
        });
    }

    [Fact]
    public void PinCheckboxOwnershipAndConcurrentSaveConflictsAreValidated()
    {
        WithFile("- [ ] First\n  - [ ] Nested\n- [ ] Other\n", (path, store) =>
        {
            var session = new MarkdownDocumentSession(path, store);
            var original = session.Text!;
            session.AddPin(original, original.IndexOf("First"), original.IndexOf("First") + 5, "First");
            var pin = Assert.Single(session.Pins);
            Assert.Throws<IOException>(() => session.SetChecked(original, original.IndexOf("[ ]", 5), true, pin.Id));
            session.SetChecked(original, original.IndexOf("[ ]"), true, pin.Id);
            Assert.Contains("[x] First", session.Text);
            Assert.Throws<IOException>(() => session.SaveText(original, "overwrite", []));
            File.AppendAllText(path, "external");
            Assert.Throws<IOException>(() => session.SetChecked(session.Text!, original.LastIndexOf("[ ]"), true));
        });
    }

    [Fact]
    public void DraftAnchorsAreCommittedOnlyOnSourceSave()
    {
        WithFile("A pinned passage here", (path, store) =>
        {
            var session = new MarkdownDocumentSession(path, store);
            session.AddPin(session.Text!, 2, 16, "pinned passage");
            var saved = Assert.Single(session.Pins);
            var draft = "new " + session.Text;
            var moved = MarkdownPinAnchors.ApplyEdit(saved, 0, 0, 4, draft);
            Assert.Equal(2, Assert.Single(store.Load(path).Pins).Start);
            session.SaveText(session.Text!, draft, [moved]);
            Assert.Equal(6, Assert.Single(store.Load(path).Pins).Start);
            Assert.Equal(draft, File.ReadAllText(path));
        });
    }

    [Fact]
    public void ConcurrentCheckboxChangesSerializeWithoutLosingEitherLine()
    {
        WithFile("- [ ] First\n- [ ] Second", (path, store) =>
        {
            var session = new MarkdownDocumentSession(path, store);
            var source = session.Text!;
            Parallel.Invoke(() => session.SetChecked(source, source.IndexOf("[ ]"), true),
                () => session.SetChecked(source, source.LastIndexOf("[ ]"), true));
            Assert.Equal("- [x] First\n- [x] Second", File.ReadAllText(path));
        });
    }

    [Fact]
    public void SourceMappingDoesNotInflateLongPlainTextIntoMultiMegabyteJson()
    {
        var html = MarkdownPreviewHtml.CreateDisplayDocument(new string('a', 300_000), "large", true, true);
        Assert.True(html.Length < 400_000);
    }

    [Fact]
    public void UnsupportedMetadataIsPreservedInsteadOfOverwritten()
    {
        WithFile("A pinned passage", (path, store) =>
        {
            store.Save(new() { Version = 2, DocumentPath = path });
            var metadataFile = Directory.GetFiles(Path.GetDirectoryName(path)!, "*.json").Single();
            var bytes = File.ReadAllBytes(metadataFile);
            var session = new MarkdownDocumentSession(path, store);
            Assert.NotNull(session.Error);
            Assert.Throws<IOException>(() => session.AddPin(session.Text!, 2, 8, "pinned"));
            Assert.Equal(bytes, File.ReadAllBytes(metadataFile));
        });
    }

    private static void WithFile(string text, Action<string, MarkdownPinStore> action)
    {
        var folder = Directory.CreateTempSubdirectory("2do3-pins-test-");
        var path = Path.Combine(folder.FullName, "source.md");
        try { File.WriteAllText(path, text, new UTF8Encoding(true)); action(path, new MarkdownPinStore(folder.FullName)); }
        finally
        {
            foreach (var file in folder.EnumerateFiles()) file.Delete();
            folder.Delete();
        }
    }
}
