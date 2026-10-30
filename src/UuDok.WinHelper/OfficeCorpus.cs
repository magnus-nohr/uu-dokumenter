using System.Runtime.InteropServices;

namespace UuDok.WinHelper;

/// <summary>
/// Lager syntetiske testfiler med ekte Word/PowerPoint, slik at korpuset inneholder Office sine egne
/// utvidelser (a16:creationId, p14, w14 osv.). Bildene kommer fra UuDok.Tools (corpus/img).
/// </summary>
public static class OfficeCorpus
{
    const int T = -1, F = 0;

    public static object Create(string outDir)
    {
        var img = Path.Combine(Path.GetFullPath(outDir), "img");
        if (!Directory.Exists(img)) throw new DirectoryNotFoundException("Kjør først «UuDok.Tools images» for å lage corpus/img.");
        var log = new List<string>();
        var pptx = Path.Combine(Path.GetFullPath(outDir), "office-presentasjon.pptx");
        var docx = Path.Combine(Path.GetFullPath(outDir), "office-dokument.docx");
        Pptx(pptx, img, log);
        Docx(docx, img, log);
        return new { pptx, docx, log };
    }

    /// <summary>Stor presentasjon (~400 lysbilder) ved å duplisere lysbildene i en eksisterende fil med PowerPoint.</summary>
    public static object CreateBig(string input, string output, int n)
    {
        var session = OfficeSession.Start("PowerPoint.Application", "POWERPNT");
        dynamic app = session.App;
        dynamic pres = app.Presentations.Open(Path.GetFullPath(input), F, T, F);
        try
        {
            int baseCount = pres.Slides.Count;
            int k = 0;
            while ((int)pres.Slides.Count < n)
            {
                dynamic dup = pres.Slides[(k % baseCount) + 1].Duplicate();
                dup.MoveTo(pres.Slides.Count);
                Marshal.FinalReleaseComObject(dup);
                k++;
            }
            pres.SaveAs(Path.GetFullPath(output), 24);
            return new { output, slides = (int)pres.Slides.Count };
        }
        finally
        {
            try { pres.Close(); } catch { }
            Marshal.FinalReleaseComObject(pres);
            session.QuitIfOwned(word: false);
            Marshal.FinalReleaseComObject(app);
        }
    }

    static void Try(List<string> log, string what, Action a)
    {
        try { a(); log.Add("OK: " + what); }
        catch (Exception ex) { log.Add($"FEIL: {what}: {ex.Message}"); }
    }

    static void Pptx(string path, string img, List<string> log)
    {
        var session = OfficeSession.Start("PowerPoint.Application", "POWERPNT");
        dynamic app = session.App;
        dynamic pres = app.Presentations.Add(F);
        try
        {
            dynamic L(int i) => pres.SlideMaster.CustomLayouts[i];
            // 1 Tittellysbilde
            dynamic s1 = pres.Slides.AddSlide(1, L(1));
            s1.Shapes.Placeholders[1].TextFrame.TextRange.Text = "Naturfag 9. trinn";
            s1.Shapes.Placeholders[2].TextFrame.TextRange.Text = "Planter og energi";

            // 2 Tittel og innhold + bilde uten alt-tekst
            dynamic s2 = pres.Slides.AddSlide(2, L(2));
            s2.Shapes.Placeholders[1].TextFrame.TextRange.Text = "Cellens deler";
            s2.Shapes.Placeholders[2].TextFrame.TextRange.Text = "Alle planteceller har cellevegg og cellekjerne.";
            Try(log, "bilde uten alt-tekst", () =>
            {
                dynamic p = s2.Shapes.AddPicture(Path.Combine(img, "illustrasjon-celle.png"), F, T, 520, 140, 360, 360);
                p.AlternativeText = "";
                p.Name = "Bilde celle";
            });

            // 3 Tomt oppsett med KI-bilde som har tittel og undertekst i pikslene (ingen tittelplassholder)
            dynamic s3 = pres.Slides.AddSlide(3, L(7));
            Try(log, "KI-tittelbilde fullskjerm", () =>
            {
                float w = pres.PageSetup.SlideWidth, h = pres.PageSetup.SlideHeight;
                dynamic p = s3.Shapes.AddPicture(Path.Combine(img, "ki-tittelbilde.png"), F, T, 0, 0, w, h);
                p.AlternativeText = "";
                p.Name = "KI-bilde";
            });

            // 4 Overlappende figurer, gruppe og animasjon
            dynamic s4 = pres.Slides.AddSlide(4, L(6));
            s4.Shapes.Placeholders[1].TextFrame.TextRange.Text = "Næringskjeden";
            Try(log, "overlapp + gruppe + animasjon", () =>
            {
                dynamic a = s4.Shapes.AddShape(1, 100, 200, 200, 120); a.Name = "Boks A"; a.TextFrame.TextRange.Text = "Sol";
                dynamic b = s4.Shapes.AddShape(1, 250, 260, 200, 120); b.Name = "Boks B"; b.TextFrame.TextRange.Text = "Plante";
                dynamic c = s4.Shapes.AddShape(9, 500, 200, 150, 150); c.Name = "Sirkel C"; c.TextFrame.TextRange.Text = "Dyr";
                dynamic arrow = s4.Shapes.AddConnector(1, 450, 320, 500, 275); arrow.Name = "Pil";
                dynamic g = s4.Shapes.Range(new object[] { "Sirkel C", "Pil" }).Group(); g.Name = "Gruppe dyr";
                s4.TimeLine.MainSequence.AddEffect(b, 10, 0, 1);
                s4.SlideShowTransition.EntryEffect = 1793;
            });
            Try(log, "dekorativ linje", () =>
            {
                dynamic d = s4.Shapes.AddPicture(Path.Combine(img, "dekor-linje.png"), F, T, 40, 500, 880, 22);
                d.Name = "Dekorlinje"; d.AlternativeText = "";
            });

            // 5 Tabell, lenke og graf som bilde
            dynamic s5 = pres.Slides.AddSlide(5, L(6));
            s5.Shapes.Placeholders[1].TextFrame.TextRange.Text = "Elevtall";
            Try(log, "tabell", () =>
            {
                dynamic t = s5.Shapes.AddTable(3, 2, 60, 140, 400, 120); t.Name = "Tabell elevtall";
                t.Table.Cell(1, 1).Shape.TextFrame.TextRange.Text = "Trinn";
                t.Table.Cell(1, 2).Shape.TextFrame.TextRange.Text = "Antall";
                t.Table.Cell(2, 1).Shape.TextFrame.TextRange.Text = "8."; t.Table.Cell(2, 2).Shape.TextFrame.TextRange.Text = "24";
                t.Table.Cell(3, 1).Shape.TextFrame.TextRange.Text = "9."; t.Table.Cell(3, 2).Shape.TextFrame.TextRange.Text = "31";
            });
            Try(log, "lenke «Klikk her»", () =>
            {
                dynamic tb = s5.Shapes.AddTextbox(1, 60, 300, 400, 40); tb.Name = "Lenketekst";
                tb.TextFrame.TextRange.Text = "Klikk her";
                tb.TextFrame.TextRange.ActionSettings[1].Hyperlink.Address = "https://www.uutilsynet.no/";
            });
            Try(log, "graf som bilde", () =>
            {
                dynamic p = s5.Shapes.AddPicture(Path.Combine(img, "graf-elever.png"), F, T, 500, 140, 420, 280);
                p.AlternativeText = "Diagram"; p.Name = "Grafbilde";
            });

            // 6 SmartArt + QR + notat
            dynamic s6 = pres.Slides.AddSlide(6, L(6));
            s6.Shapes.Placeholders[1].TextFrame.TextRange.Text = "Oppgaver";
            Try(log, "SmartArt", () =>
            {
                dynamic sa = s6.Shapes.AddSmartArt(app.SmartArtLayouts[1], 60, 140, 500, 300); sa.Name = "SmartArt prosess";
                dynamic nodes = sa.SmartArt.AllNodes;
                string[] tx = ["Les", "Diskuter", "Skriv"];
                for (int i = 1; i <= Math.Min(3, (int)nodes.Count); i++) nodes[i].TextFrame2.TextRange.Text = tx[i - 1];
            });
            Try(log, "QR-kode", () =>
            {
                dynamic q = s6.Shapes.AddPicture(Path.Combine(img, "qr-oppgave.png"), F, T, 640, 160, 220, 220);
                q.AlternativeText = ""; q.Name = "QR";
            });
            Try(log, "notat", () => s6.NotesPage.Shapes.Placeholders[2].TextFrame.TextRange.Text = "Husk å dele ut ark.");

            // 7 Skjermbilde og lite bilde, og bilde med god alt-tekst
            dynamic s7 = pres.Slides.AddSlide(7, L(6));
            s7.Shapes.Placeholders[1].TextFrame.TextRange.Text = "Lagre arbeidet";
            Try(log, "skjermbilde", () =>
            {
                dynamic p = s7.Shapes.AddPicture(Path.Combine(img, "skjermbilde-lagre.png"), F, T, 60, 130, 480, 300);
                p.AlternativeText = "tekst, datamaskin, tegnefilm, innendørs"; p.Name = "Skjermbilde";
            });
            Try(log, "lite uskarpt bilde", () =>
            {
                dynamic p = s7.Shapes.AddPicture(Path.Combine(img, "lite-uskarpt.png"), F, T, 600, 140, 80, 60);
                p.AlternativeText = ""; p.Name = "Lite bilde";
            });
            Try(log, "samme bilde, god alt-tekst", () =>
            {
                dynamic p = s7.Shapes.AddPicture(Path.Combine(img, "illustrasjon-celle.png"), F, T, 600, 260, 200, 200);
                p.AlternativeText = "Planteselle med cellevegg ytterst og cellekjerne i midten."; p.Name = "Celle med god tekst";
            });

            // 8 KI-bilde med skjev tekst, oppå et tomt oppsett (vanskelig OCR)
            dynamic s8 = pres.Slides.AddSlide(8, L(7));
            Try(log, "KI-tittelbilde skjev tekst", () =>
            {
                float w = pres.PageSetup.SlideWidth, h = pres.PageSetup.SlideHeight;
                dynamic p = s8.Shapes.AddPicture(Path.Combine(img, "ki-tittelbilde-skjev.png"), F, T, 0, 0, w, h);
                p.AlternativeText = ""; p.Name = "KI-bilde skjev";
            });

            pres.SaveAs(path, 24);
        }
        finally
        {
            try { pres.Close(); } catch { }
            Marshal.FinalReleaseComObject(pres);
            session.QuitIfOwned(word: false);
            Marshal.FinalReleaseComObject(app);
        }
    }

    static void Docx(string path, string img, List<string> log)
    {
        var session = OfficeSession.Start("Word.Application", "WINWORD");
        dynamic app = session.App;
        if (session.Owned) { app.Visible = false; app.DisplayAlerts = 0; }
        dynamic doc = app.Documents.Add();
        try
        {
            dynamic Para(string text)
            {
                dynamic last = doc.Paragraphs[doc.Paragraphs.Count];
                if (((string)last.Range.Text).Trim().Length > 0) { doc.Content.InsertParagraphAfter(); last = doc.Paragraphs[doc.Paragraphs.Count]; }
                last.Range.Text = text;
                return doc.Paragraphs[doc.Paragraphs.Count];
            }
            dynamic p;
            p = Para("Prosjektplan for naturfag"); p.Range.Font.Size = 24; p.Range.Font.Bold = 1;   // falsk overskrift
            p = Para("Denne planen beskriver tre undervisningsøkter om planter.");
            p = Para("Mål for perioden"); p.Style = doc.Styles[-2];                                  // ekte Overskrift 1
            p = Para("Elevene skal kunne forklare fotosyntese med egne ord.");
            p = Para("Økt 1"); p.Range.Font.Size = 16; p.Range.Font.Bold = 1;                        // falsk overskrift nivå 2
            p = Para("- Les teksten i boka");                                                        // falsk liste
            p = Para("- Svar på spørsmålene");
            p = Para("Økt 2"); p.Style = doc.Styles[-3];                                             // ekte Overskrift 2
            p = Para("Ekte punktliste:");
            p = Para("Første punkt"); Try(log, "punktliste", () => p.Range.ListFormat.ApplyBulletDefault());
            p = Para("Andre punkt");
            p = Para("Les mer her.");
            Try(log, "lenke «her»", () =>
            {
                dynamic r = p.Range; int start = (int)r.Start + 8; dynamic hr = doc.Range(start, start + 3);
                doc.Hyperlinks.Add(Anchor: hr, Address: "https://www.udir.no/", TextToDisplay: "her");
            });
            p = Para("Se også https://www.uutilsynet.no/ for regelverk.");
            p = Para("Denne teksten har svært lav kontrast."); p.Range.Font.Color = 13421772;      // #CCCCCC
            p = Para("Figur 1: Planteselle");
            Try(log, "innebygd bilde uten alt-tekst", () =>
            {
                dynamic r = Para("").Range;
                dynamic ils = doc.InlineShapes.AddPicture(FileName: Path.Combine(img, "illustrasjon-celle.png"), LinkToFile: false, SaveWithDocument: true, Range: r);
                ils.Width = 200; ils.Height = 200; ils.AlternativeText = "";
            });
            p = Para("Elevtall per trinn:");
            Try(log, "tabell", () =>
            {
                dynamic r = Para("").Range;
                dynamic t = doc.Tables.Add(r, 3, 2);
                t.Borders.Enable = 1;
                t.Cell(1, 1).Range.Text = "Trinn"; t.Cell(1, 2).Range.Text = "Antall";
                t.Cell(2, 1).Range.Text = "8."; t.Cell(2, 2).Range.Text = "24";
                t.Cell(3, 1).Range.Text = "9."; t.Cell(3, 2).Range.Text = "31";
                t.Cell(2, 1).Merge(t.Cell(3, 1));
            });
            p = Para("Formel: ");
            Try(log, "formel", () =>
            {
                dynamic r = Para("a^2+b^2=c^2").Range;
                dynamic om = doc.OMaths.Add(r); om.OMaths[1].BuildUp();
            });
            Try(log, "topptekst", () => doc.Sections[1].Headers[1].Range.Text = "Naturfag – skoleåret");
            Try(log, "kommentar", () => doc.Comments.Add(doc.Paragraphs[2].Range, "Syntetisk kommentar."));
            Try(log, "bokmerke", () => doc.Bookmarks.Add("maal", doc.Paragraphs[3].Range));
            Try(log, "felt (dato)", () => doc.Fields.Add(Para("Dato: ").Range.Characters.Last, 31));
            Try(log, "innholdskontroll", () => { dynamic cc = doc.ContentControls.Add(1, Para("Navn på gruppe").Range); cc.Title = "Gruppe"; });
            Try(log, "tekstboks", () => { dynamic tb = doc.Shapes.AddTextbox(1, 350, 60, 160, 60); tb.TextFrame.TextRange.Text = "Merk: dette er en tekstboks."; });
            Try(log, "spor endringer", () =>
            {
                doc.TrackRevisions = true;
                Para("Denne setningen er lagt til med spor endringer.");
                doc.TrackRevisions = false;
            });
            Try(log, "bildetekst-bilde: KI-tittelbilde i Word", () =>
            {
                dynamic r = Para("").Range;
                dynamic ils = doc.InlineShapes.AddPicture(FileName: Path.Combine(img, "graf-elever.png"), LinkToFile: false, SaveWithDocument: true, Range: r);
                ils.Width = 300; ils.Height = 200; ils.AlternativeText = "graf";
            });
            doc.SaveAs2(path, 16);
        }
        finally
        {
            try { doc.Close(SaveChanges: 0); } catch { }
            Marshal.FinalReleaseComObject(doc);
            session.QuitIfOwned(word: true);
            Marshal.FinalReleaseComObject(app);
        }
    }
}
