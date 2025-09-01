using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using static SprintReportGenerator.Services.JiraClient;

namespace SprintReportGenerator.Services
{
    /// <summary>
    /// Generates the report from a template: replaces basic fields, appends SUMMARY and 3 tables.
    /// </summary>
    public class OpenXmlTemplateProcessor
    {
        public string Generate(string templatePath, Models.TemplateData data, IReadOnlyList<JiraIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
                throw new FileNotFoundException("Template not found.", templatePath);

            string outDir = Path.GetDirectoryName(templatePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string safeProj = (data.ProjectName ?? "Project").Replace(Path.GetInvalidFileNameChars(), '_');
            string safeSprint = (data.SprintName ?? "Sprint").Replace(Path.GetInvalidFileNameChars(), '_');

            string output = Path.Combine(outDir, $"Sprint {safeSprint} {safeProj} Test Kapanış Raporu.docx");
            File.Copy(templatePath, output, true);

            // Allowed types (Bug / Improvement / Story)
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Bug", "Improvement", "Story" };

            var filtered = (issues ?? Array.Empty<JiraIssue>())
                           .Where(i => allowed.Contains(i.Type))
                           .ToList();

            using (var doc = WordprocessingDocument.Open(output, true))
            {
                EnableUpdateFieldsOnOpen(doc);

                var body = doc.MainDocumentPart!.Document.Body ?? (doc.MainDocumentPart.Document.Body = new Body());

                // --- Replace placeholders (only these four) ---
                ReplaceToken(body, "{{PROJECT}}", data.ProjectName ?? "");
                ReplaceToken(body, "{{FORM_DATE}}", data.ReportDate ?? "");
                ReplaceToken(body, "{{MEMBER_NAME}}", data.MemberName ?? "");
                ReplaceToken(body, "{{SPRINT}}", data.SprintName ?? "");

                // --- SUMMARY (before tables) ---
                AppendSummaryTrSection(body, data, filtered);

                // --- Tables ---
                AppendSection(doc, body, "Test Senaryo durumları", filtered, headingLevel: 2);

                AppendSection(doc, body, "Sprint Kapsamında Açılan Buglar",
                             filtered.Where(i => i.Type.Equals("Bug", StringComparison.OrdinalIgnoreCase)),
                             headingLevel: 2);

                AppendSection(doc, body, "Sprint Kapsamında Açılan Improvement Kayıtları",
                             filtered.Where(i => i.Type.Equals("Improvement", StringComparison.OrdinalIgnoreCase)),
                             headingLevel: 2);

                doc.MainDocumentPart.Document.Save();
            }

            return output;
        }

        // Robust token replacement across split runs (w:t)
        private static void ReplaceToken(Body body, string token, string value)
        {
            if (body == null || string.IsNullOrEmpty(token)) return;

            foreach (var p in body.Descendants<Paragraph>())
            {
                var runs = p.Descendants<Run>().ToList();
                if (runs.Count == 0) continue;

                var texts = runs.SelectMany(r => r.Elements<Text>()).ToList();
                if (texts.Count == 0) continue;

                var original = string.Concat(texts.Select(t => t.Text ?? string.Empty));
                if (original.IndexOf(token, StringComparison.Ordinal) < 0) continue;

                var replaced = original.Replace(token, value ?? string.Empty, StringComparison.Ordinal);

                var firstProps = runs[0].RunProperties?.CloneNode(true) as RunProperties;
                foreach (var r in runs) r.Remove();

                var newRun = new Run(new Text(replaced) { Space = SpaceProcessingModeValues.Preserve });
                if (firstProps != null) newRun.RunProperties = (RunProperties)firstProps.CloneNode(true);

                p.AppendChild(newRun);
            }
        }

        /// <summary>
        /// Appends a section to the specified WordprocessingDocument body, including a heading and a table of Jira issues.
        /// </summary>
        private static void AppendSection(WordprocessingDocument doc, Body body, string heading, IEnumerable<JiraIssue> source, int headingLevel = 2)
        {
            body.AppendChild(MakeHeadingParagraph(heading, headingLevel));

            var list = source?.ToList() ?? new List<JiraIssue>();
            var table = BuildIssuesTable(list);
            body.AppendChild(table);

            body.AppendChild(new Paragraph(new Run(new Text(" "))));
        }

        /// <summary>
        /// Creates a paragraph styled as a heading with the specified text and heading level.
        /// </summary>
        private static Paragraph MakeHeadingParagraph(string text, int level = 2)
        {
            // clamp 1..9  (Heading1..Heading9)
            level = Math.Max(1, Math.Min(level, 9));

            var run = new Run(new Text(text ?? string.Empty))
            {
                RunProperties = new RunProperties(new Bold())
            };

            var p = new Paragraph(run);
            var props = new ParagraphProperties();

            // Built-in heading style (TOC \o "1-3" uses this)
            props.ParagraphStyleId = new ParagraphStyleId { Val = $"Heading{level}" };

            // Fallback: also set outline level (0 = H1, 1 = H2, ...)
            props.OutlineLevel = new OutlineLevel { Val = (byte)(level - 1) };

            p.ParagraphProperties = props;
            return p;
        }

        // Builds a Turkish breakdown like: "8’i Closed, 1’i Cancelled" and skips zeros.
        private static string BuildBreakdownTr(int closed, int cancelled, int inProgress, int inQa)
        {
            var parts = new List<string>(4);
            if (closed > 0) parts.Add($"{closed}’i Closed");
            if (cancelled > 0) parts.Add($"{cancelled}’i Cancelled");
            if (inProgress > 0) parts.Add($"{inProgress}’i In Progress");
            if (inQa > 0) parts.Add($"{inQa}’i In Q&A");
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Enables the automatic updating of fields when the WordprocessingDocument is opened.
        /// </summary>
        private static void EnableUpdateFieldsOnOpen(WordprocessingDocument doc)
        {
            var part = doc.MainDocumentPart!.DocumentSettingsPart
                       ?? doc.MainDocumentPart.AddNewPart<DocumentSettingsPart>();
            if (part.Settings == null) part.Settings = new Settings();

            var upd = part.Settings.Elements<UpdateFieldsOnOpen>().FirstOrDefault();
            if (upd == null)
                part.Settings.AppendChild(new UpdateFieldsOnOpen { Val = true });
            else
                upd.Val = true;

            // ensure the setting is persisted
            part.Settings.Save();
        }


        // Bullet paragraph without numbering part (uses • character + küçük girinti)
        private static Paragraph MakeBulletParagraph(string text)
        {
            var p = new Paragraph(new Run(new Text("• " + (text ?? string.Empty))
            {
                Space = SpaceProcessingModeValues.Preserve
            }));

            p.ParagraphProperties = new ParagraphProperties(
                new Indentation { Left = "720", Hanging = "360" }   // ~0.5" girinti, okunur görünüm
            );

            return p;
        }

        // ======= SUMMARY (TR) =======
        // ======= SUMMARY (TR) =======
        private static void AppendSummaryTrSection(Body body, Models.TemplateData data, List<JiraIssue> items)
        {
            // Add summary heading so it shows up in TOC
            body.AppendChild(MakeHeadingParagraph("Özet", 2));

            // Build readable date lead:
            // both present : "12 Mayıs 2025 – 26 Mayıs 2025 tarihleri arasında"
            // only start   : "12 Mayıs 2025 tarihinden itibaren"
            // only end     : "26 Mayıs 2025 tarihine kadar"
            // none         : "" (omit)
            string BuildDateLead(string start, string end)
            {
                bool hasStart = !string.IsNullOrWhiteSpace(start);
                bool hasEnd = !string.IsNullOrWhiteSpace(end);

                if (hasStart && hasEnd) return $"{start} – {end} tarihleri arasında";
                if (hasStart) return $"{start} tarihinden itibaren";
                if (hasEnd) return $"{end} tarihine kadar";
                return string.Empty;
            }

            var dateLead = BuildDateLead(data.SprintStartDate, data.SprintEndDate);

            var proj = string.IsNullOrWhiteSpace(data.ProjectName) ? "Proje" : data.ProjectName!;
            var sprint = string.IsNullOrWhiteSpace(data.SprintName) ? "Sprint" : data.SprintName!;
            int total = items.Count;
            int closed = items.Count(i => IsClosedStatus(i.Status));

            var envPrefix = "Proven Test ve Pre-production";
            // Prefix with date lead only when present
            var prefix = string.IsNullOrEmpty(dateLead)
                ? $"{envPrefix} ortamında koşulan "
                : $"{dateLead} {envPrefix} ortamında koşulan ";

            var p1 = $"{prefix}{proj} projesi {sprint} sprintine ait {total} kayıt bulunmaktadır. Bunlardan {closed}’i Closed durumundadır.";
            body.AppendChild(new Paragraph(new Run(new Text(p1) { Space = SpaceProcessingModeValues.Preserve })));

            // Existing heading stays as-is
            body.AppendChild(MakeHeadingParagraph("Test genel, hata bildirimi ve iyileştirme durumu", 2));

            // bullets by type (unchanged)
            static string BreakdownTr(int cl, int cn, int ip, int iq, int bl)
            {
                var parts = new List<string>(5);
                if (cl > 0) parts.Add($"{cl}’i Closed");
                if (cn > 0) parts.Add($"{cn}’i Cancelled");
                if (ip > 0) parts.Add($"{ip}’i In Progress");
                if (iq > 0) parts.Add($"{iq}’i In Q&A");
                if (bl > 0) parts.Add($"{bl}’i Blocked");
                return string.Join(", ", parts);
            }

            void TypeBlock(string label, IEnumerable<JiraIssue> src, string prefixText)
            {
                var list = src.ToList();
                if (list.Count == 0) return;

                int cl = list.Count(i => IsClosedStatus(i.Status));
                int cn = list.Count(i => IsCancelledStatus(i.Status));
                int ip = list.Count(i => IsInProgressStatus(i.Status));
                int iq = list.Count(i => IsInQaStatus(i.Status));
                int bl = list.Count(i => IsBlockedStatus(i.Status));

                var line = $"{prefixText} {list.Count} adet {label} yapılmıştır.";
                var b = BreakdownTr(cl, cn, ip, iq, bl);
                if (!string.IsNullOrWhiteSpace(b)) line += $" {b} durumundadır.";
                body.AppendChild(MakeBulletParagraph(line));
            }

            TypeBlock("Bug",
                items.Where(i => i.Type?.Equals("Bug", StringComparison.OrdinalIgnoreCase) == true),
                "Testler sırasında");

            TypeBlock("iyileştirme talebi",
                items.Where(i => i.Type?.Equals("Improvement", StringComparison.OrdinalIgnoreCase) == true),
                "Testler sırasında");

            TypeBlock("Story",
                items.Where(i => i.Type?.Equals("Story", StringComparison.OrdinalIgnoreCase) == true),
                "Testler sırasında");

            body.AppendChild(new Paragraph(new Run(new Text(" "))));
        }



        private static Table BuildIssuesTable(List<JiraIssue> items)
        {
            var table = new Table();

            var props = new TableProperties(
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 6 },
                    new LeftBorder { Val = BorderValues.Single, Size = 6 },
                    new BottomBorder { Val = BorderValues.Single, Size = 6 },
                    new RightBorder { Val = BorderValues.Single, Size = 6 },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 6 },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 6 }
                ),
                new TableWidth { Type = TableWidthUnitValues.Auto, Width = "0" }
            );
            table.AppendChild(props);

            // Header
            var header = new TableRow();
            header.Append(
                MakeHeaderCell("Project"),
                MakeHeaderCell("Issue Type"),
                MakeHeaderCell("Key"),
                MakeHeaderCell("Summary"),
                MakeHeaderCell("Status")
            );
            table.AppendChild(header);

            if (items.Count == 0)
            {
                var row = new TableRow();
                var cell = MakeCell("(No issues)");
                if (cell.TableCellProperties == null)
                    cell.TableCellProperties = new TableCellProperties();
                cell.TableCellProperties.GridSpan = new GridSpan { Val = 5 };
                row.Append(cell);
                table.Append(row);
                return table;
            }

            foreach (var i in items)
            {
                var row = new TableRow();
                row.Append(
                    MakeCell(i.Project ?? string.Empty),
                    MakeCell(i.Type ?? string.Empty),
                    MakeCell(i.Key ?? string.Empty),
                    MakeCell(i.Summary ?? string.Empty),
                    MakeStatusCell(i.Status ?? string.Empty)
                );
                table.AppendChild(row);
            }

            return table;
        }

        private static TableCell MakeHeaderCell(string text)
        {
            var run = new Run(new Text(text ?? string.Empty))
            {
                RunProperties = new RunProperties(new Bold())
            };
            var p = new Paragraph(run);

            var cellProps = new TableCellProperties(
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center },
                new Shading
                {
                    Fill = ColorGrayHeader,              // darker than blocked
                    Val = ShadingPatternValues.Clear,
                    Color = "auto"
                });

            var cell = new TableCell(p) { TableCellProperties = cellProps };
            return cell;
        }

        private static TableCell MakeCell(string text)
        {
            var p = new Paragraph(new Run(new Text(text ?? string.Empty)));
            var cell = new TableCell(p)
            {
                TableCellProperties = new TableCellProperties(
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center })
            };
            return cell;
        }

        private static TableCell MakeStatusCell(string status)
        {
            var fill = GetStatusFill(status);

            var p = new Paragraph(new Run(new Text(status ?? string.Empty)));
            var cell = new TableCell(p);

            cell.TableCellProperties = new TableCellProperties(
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center },
                new Shading
                {
                    Fill = fill,
                    Val = ShadingPatternValues.Clear,
                    Color = "auto"
                });

            return cell;
        }

        // ======= STATUS helpers & color mapping =======
        // ---- status colors ----
        private const string ColorGreenClosed = "92D050"; // closed
        private const string ColorBlueCancel = "1E90FF"; // cancelled/FPA
        private const string ColorYellowOpen = "FFFF00"; // others
        private const string ColorGrayBlocked = "C9C9C9"; // blocked cells
        private const string ColorGrayHeader = "A6A6A6"; // header (daha koyu)

        // already existing helpers:
        private static bool IsClosedStatus(string? s) => !string.IsNullOrWhiteSpace(s) &&
            (s!.Trim().Equals("Closed", StringComparison.OrdinalIgnoreCase) ||
             s.Trim().Equals("Done", StringComparison.OrdinalIgnoreCase));

        private static bool IsCancelledStatus(string? s) => !string.IsNullOrWhiteSpace(s) &&
            (s!.Trim().Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ||
             s.Trim().Equals("Canceled", StringComparison.OrdinalIgnoreCase) ||
             s.Trim().Equals("False Positive Approval", StringComparison.OrdinalIgnoreCase));

        private static bool IsInProgressStatus(string? s) => !string.IsNullOrWhiteSpace(s) &&
            (s!.Trim().Equals("In Progress", StringComparison.OrdinalIgnoreCase) ||
             s.Trim().Equals("Dev Completed", StringComparison.OrdinalIgnoreCase));

        private static bool IsInQaStatus(string? s) => !string.IsNullOrWhiteSpace(s) &&
            (s!.Trim().Equals("In Q&A", StringComparison.OrdinalIgnoreCase) ||
             s.Trim().Equals("In QA", StringComparison.OrdinalIgnoreCase));

        private static bool IsBlockedStatus(string? s) =>
            !string.IsNullOrWhiteSpace(s) && s!.Trim().Equals("Blocked", StringComparison.OrdinalIgnoreCase);

        // use helpers here
        private static string GetStatusFill(string status)
        {
            if (IsClosedStatus(status)) return ColorGreenClosed;
            if (IsCancelledStatus(status)) return ColorBlueCancel;
            if (IsBlockedStatus(status)) return ColorGrayBlocked;
            return ColorYellowOpen;
        }

    }

    internal static class PathSafeExtensions
    {
        public static string Replace(this string s, char[] chars, char withChar)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var result = s.ToCharArray();
            var invalids = new HashSet<char>(chars);
            for (int i = 0; i < result.Length; i++)
                if (invalids.Contains(result[i])) result[i] = withChar;
            return new string(result);
        }
    }
}
