using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace UuDok.WinHelper;

public sealed record ShapeInfo(int Id, string Name, int ZOrder, float Left, float Top, float Width, float Height, float Rotation,
    int Type, bool Visible, string? AltText, bool? Decorative, string? TextHash, int TextLength, string? Text, bool IsPlaceholder,
    int? PlaceholderType, bool HasAnimation);

public sealed record SlideStructure(int Index, bool Hidden, bool HasTitle, string? TitleText, int AnimationCount,
    int TransitionEffect, int MediaCount, string Png, List<ShapeInfo> Shapes);

public sealed record PptxRender(string Engine, string Version, string Build, float SlideWidthPt, float SlideHeightPt,
    int SlideCount, List<SlideStructure> Slides, bool OfficeWasRunning);

public sealed record ParagraphInfo(int Index, int OutlineLevel, string Style, int EndPage, string TextHash, int Length);

public sealed record DocxRender(string Engine, string Version, string Build, int Pages, int Paragraphs, string TextHash,
    int InlineShapes, int Shapes, int Hyperlinks, int Fields, int Bookmarks, int Comments, int Revisions, int ContentControls,
    int Tables, List<string> InlineAltTexts, List<ParagraphInfo> ParagraphInfo, string Pdf, List<string> Pngs);

/// <summary>
/// Rendering og strukturuttrekk via Word/PowerPoint (COM). Filene åpnes skrivebeskyttet, uten vindu,
/// med makroer tvunget av (AutomationSecurity = msoAutomationSecurityForceDisable).
/// </summary>
public static class OfficeRender
{
    const int MsoTrue = -1, MsoFalse = 0, ForceDisable = 3;

    public static object Info()
    {
        var res = new Dictionary<string, object?>();
        foreach (var (prog, key, proc) in new[] { ("Word.Application", "word", "WINWORD"), ("PowerPoint.Application", "powerpoint", "POWERPNT") })
        {
            OfficeSession? s = null;
            try
            {
                s = OfficeSession.Start(prog, proc);
                res[key] = new { version = (string)s.App.Version, build = (string)s.App.Build, ownedProcess = s.Owned };
                s.QuitIfOwned(key == "word");
            }
            catch (Exception ex) { res[key] = new { error = ex.Message }; }
            finally { Release(s?.App); }
        }
        return res;
    }

    public static PptxRender RenderPptx(string input, string outDir, int width)
    {
        Directory.CreateDirectory(outDir);
        var session = OfficeSession.Start("PowerPoint.Application", "POWERPNT");
        var wasRunning = !session.Owned;
        dynamic app = session.App;
        dynamic? pres = null;
        try
        {
            try { app.AutomationSecurity = ForceDisable; } catch { }
            try { app.DisplayAlerts = 1; } catch { } // ppAlertsNone
            pres = app.Presentations.Open(Path.GetFullPath(input), MsoTrue, MsoFalse, MsoFalse);
            float sw = pres.PageSetup.SlideWidth, sh = pres.PageSetup.SlideHeight;
            int height = (int)Math.Round(width * sh / sw);
            var slides = new List<SlideStructure>();
            int count = pres.Slides.Count;
            for (int i = 1; i <= count; i++)
            {
                dynamic s = pres.Slides[i];
                var png = Path.Combine(Path.GetFullPath(outDir), $"slide-{i:D3}.png");
                s.Export(png, "PNG", width, height);
                var animated = new HashSet<int>();
                int anim = 0;
                try
                {
                    dynamic seq = s.TimeLine.MainSequence;
                    anim = seq.Count;
                    for (int k = 1; k <= anim; k++) { try { animated.Add((int)seq[k].Shape.Id); } catch { } }
                }
                catch { }
                var shapes = new List<ShapeInfo>();
                int media = 0;
                int n = s.Shapes.Count;
                for (int j = 1; j <= n; j++)
                {
                    dynamic sh2 = s.Shapes[j];
                    int type = sh2.Type;
                    if (type == 16) media++;
                    string? text = null;
                    try { if ((int)sh2.HasTextFrame == MsoTrue) text = (string)sh2.TextFrame.TextRange.Text; } catch { }
                    bool? deco = null;
                    try { deco = (int)sh2.Decorative == MsoTrue; } catch { }
                    int? phType = null;
                    bool isPh = type == 14;
                    if (isPh) { try { phType = (int)sh2.PlaceholderFormat.Type; } catch { } }
                    int id = sh2.Id;
                    shapes.Add(new ShapeInfo(id, (string)sh2.Name, (int)sh2.ZOrderPosition, (float)sh2.Left, (float)sh2.Top,
                        (float)sh2.Width, (float)sh2.Height, (float)sh2.Rotation, type, (int)sh2.Visible == MsoTrue,
                        Safe(() => (string)sh2.AlternativeText), deco, text == null ? null : Hash(text), text?.Length ?? 0,
                        text, isPh, phType, animated.Contains(id)));
                    Release(sh2);
                }
                bool hasTitle = (int)s.Shapes.HasTitle == MsoTrue;
                string? title = null;
                if (hasTitle) title = Safe(() => (string)s.Shapes.Title.TextFrame.TextRange.Text);
                slides.Add(new SlideStructure(i, Safe(() => (int)s.SlideShowTransition.Hidden == MsoTrue), hasTitle, title, anim,
                    Safe(() => (int)s.SlideShowTransition.EntryEffect), media, png, shapes));
                Release(s);
            }
            return new PptxRender("PowerPoint", (string)app.Version, (string)app.Build, sw, sh, count, slides, wasRunning);
        }
        finally
        {
            try { pres?.Close(); } catch { }
            Release(pres);
            session.QuitIfOwned(word: false);
            Release(app);
        }
    }

    public static DocxRender RenderDocx(string input, string outDir, int dpi)
    {
        Directory.CreateDirectory(outDir);
        var session = OfficeSession.Start("Word.Application", "WINWORD");
        dynamic app = session.App;
        dynamic? doc = null;
        try
        {
            if (session.Owned) { app.Visible = false; app.DisplayAlerts = 0; }
            try { app.AutomationSecurity = ForceDisable; } catch { }
            // Observert (Word 16.0.20527): InlineShape.AlternativeText gir COM-feil når dokumentet åpnes med
            // Visible:false. I egen, usynlig Word-instans er dokumentvinduet uansett skjult, så Visible utelates da.
            doc = session.Owned
                ? app.Documents.Open(FileName: Path.GetFullPath(input), ConfirmConversions: false, ReadOnly: true,
                    AddToRecentFiles: false, OpenAndRepair: false, NoEncodingDialog: true)
                : app.Documents.Open(FileName: Path.GetFullPath(input), ConfirmConversions: false, ReadOnly: true,
                    AddToRecentFiles: false, Visible: false, OpenAndRepair: false, NoEncodingDialog: true);
            doc.Repaginate();
            int pages = doc.ComputeStatistics(2);
            var paras = new List<ParagraphInfo>();
            int pc = doc.Paragraphs.Count;
            var all = new StringBuilder();
            int idx = 0;
            foreach (dynamic p in doc.Paragraphs)
            {
                idx++;
                dynamic r = p.Range;
                string t = r.Text;
                all.Append(t);
                string style = Safe(() => (string)p.Style.NameLocal) ?? "";
                paras.Add(new ParagraphInfo(idx, (int)p.OutlineLevel, style, (int)r.Information(3), Hash(t), t.Length));
                Release(r); Release(p);
            }
            var alts = new List<string>();
            int nIls = doc.InlineShapes.Count;
            for (int k = 1; k <= nIls; k++)
            {
                dynamic ils = doc.InlineShapes[k];
                try { alts.Add((string)ils.AlternativeText); }
                catch (Exception ex) { alts.Add("!feil: " + ex.Message); }
            }
            var pdf = Path.Combine(Path.GetFullPath(outDir), "document.pdf");
            doc.ExportAsFixedFormat(OutputFileName: pdf, ExportFormat: 17, OpenAfterExport: false, OptimizeFor: 0,
                CreateBookmarks: 1, DocStructureTags: true, BitmapMissingFonts: true);
            var res = new DocxRender("Word", (string)app.Version, (string)app.Build, pages, pc, Hash(all.ToString()),
                (int)doc.InlineShapes.Count, (int)doc.Shapes.Count, (int)doc.Hyperlinks.Count, (int)doc.Fields.Count,
                (int)doc.Bookmarks.Count, (int)doc.Comments.Count, (int)doc.Revisions.Count, (int)doc.ContentControls.Count,
                (int)doc.Tables.Count, alts, paras, pdf, []);
            doc.Close(SaveChanges: 0);
            Release(doc); doc = null;
            var pngs = PdfRaster.Render(pdf, outDir, dpi).GetAwaiter().GetResult();
            return res with { Pngs = pngs };
        }
        finally
        {
            try { doc?.Close(SaveChanges: 0); } catch { }
            Release(doc);
            session.QuitIfOwned(word: true);
            Release(app);
        }
    }

    static T? Safe<T>(Func<T> f) { try { return f(); } catch { return default; } }
    static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..16];
    static void Release(object? o) { if (o != null && Marshal.IsComObject(o)) try { Marshal.FinalReleaseComObject(o); } catch { } }
}
