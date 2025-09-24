// Services/OpenXmlTemplateProcessor.cs
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
    /// Generates the report from a DOCX template:
    /// - replaces basic tokens
    /// - appends a Turkish summary section
    /// - appends three tables (all issues, Bugs, Improvements)
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

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Bug", "Improvement", "Story" };

            var filtered = (issues ?? Array.Empty<JiraIssue>())
                           .Where(i => allowed.Contains(i.Type))
                           .ToList();

            using (var doc = WordprocessingDocument.Open(output, true))
            {
                EnableUpdateFieldsOnOpen(doc);

                var body = doc.MainDocumentPart!.Document.Body ?? (doc.MainDocumentPart.Document.Body = new Body());

                // Replace placeholders
                ReplaceToken(body, "{{PROJECT}}", data.ProjectName ?? "");
                ReplaceToken(body, "{{FORM_DATE}}", data.ReportDate ?? "");
                ReplaceToken(body, "{{MEMBER_NAME}}", data.MemberName ?? "");
                ReplaceToken(body, "{{SPRINT}}", data.SprintName ?? "");

                // >>> Keep TOC alone on first page: start content on a NEW page
                AppendPageBreak(body);

                // SUMMARY
                AppendSummaryTrSection(body, data, filtered);

                // TABLES
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

        private static Paragraph BlankLine() => new Paragraph(new Run(new Text("")));

        private static void AppendPageBreak(Body body)
        {
            // insert a page break so TOC stays on its own page
            var p = new Paragraph(new Run(new Break { Type = BreakValues.Page }));
            body.AppendChild(p);
        }
        private static void AppendLineBreak()
        {
            
        }
        // Robust token replacement across split runs
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
        /// Section with heading + table + spacing (adds 1 blank line after heading and after table)
        /// </summary>
        private static void AppendSection(WordprocessingDocument doc, Body body, string heading, IEnumerable<JiraIssue> source, int headingLevel = 2)
        {
            body.AppendChild(MakeHeadingParagraph(heading, headingLevel));
            body.AppendChild(BlankLine()); // spacing after heading

            var list = source?.ToList() ?? new List<JiraIssue>();
            var table = BuildIssuesTable(list);
            body.AppendChild(table);

            body.AppendChild(BlankLine()); // spacing after section content
        }

        private static Paragraph MakeHeadingParagraph(string text, int level = 2)
        {
            level = Math.Max(1, Math.Min(level, 9));

            var run = new Run(new Text(text ?? string.Empty))
            {
                RunProperties = new RunProperties(new Bold())
            };

            var p = new Paragraph(run);
            var props = new ParagraphProperties
            {
                ParagraphStyleId = new ParagraphStyleId { Val = $"Heading{level}" },
                OutlineLevel = new OutlineLevel { Val = (byte)(level - 1) },

                SpacingBetweenLines = new SpacingBetweenLines { After = "240" } //empty space after headers
            };

            p.ParagraphProperties = props;
            return p;
        }

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

            part.Settings.Save();
        }

        private static Paragraph MakeBulletParagraph(string text)
        {
            var p = new Paragraph(new Run(new Text("• " + (text ?? string.Empty))
            {
                Space = SpaceProcessingModeValues.Preserve
            }));

            p.ParagraphProperties = new ParagraphProperties(
                new Indentation { Left = "720", Hanging = "360" }, // ~0.5" indent
                new SpacingBetweenLines { Before = "120", After = "120" }
            );

            return p;
        }

        // ===== SUMMARY (TR) =====
        private static void AppendSummaryTrSection(Body body, Models.TemplateData data, List<JiraIssue> items)
        {
            body.AppendChild(MakeHeadingParagraph("Özet", 2));
            body.AppendChild(BlankLine()); // spacing after "Özet" heading

            static string BuildDateLead(string start, string end)
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
            var prefix = string.IsNullOrEmpty(dateLead)
                ? $"{envPrefix} ortamında koşulan "
                : $"{dateLead} {envPrefix} ortamında koşulan ";

            var p1 = $"{prefix}{proj} projesi {sprint} sprintine ait {total} kayıt bulunmaktadır. Bunlardan {closed}’i Closed durumundadır.";
            var pLead = new Paragraph(new Run(new Text(p1)
            {
                Space = SpaceProcessingModeValues.Preserve
            }));
            pLead.ParagraphProperties ??= new ParagraphProperties();
            pLead.ParagraphProperties.SpacingBetweenLines = new SpacingBetweenLines
            {
                After = "240"   // 240 twips ≈ 12pt ≈ ~1 line
            };
            body.AppendChild(pLead);


            // Next heading + blank
            body.AppendChild(MakeHeadingParagraph("Test genel, hata bildirimi ve iyileştirme durumu", 2));
            body.AppendChild(BlankLine());

            static string BreakdownTr(int cl, int cn, int ip, int iq, int bl, int op, int de)
            {
                var parts = new List<string>(7);
                if (cl > 0) parts.Add($"{cl}’i Closed");
                if (cn > 0) parts.Add($"{cn}’i Cancelled");
                if (ip > 0) parts.Add($"{ip}’i In Progress");
                if (iq > 0) parts.Add($"{iq}’i In Q&A");
                if (bl > 0) parts.Add($"{bl}’i Blocked");
                if (op > 0) parts.Add($"{op}’i Open");
                if (de > 0) parts.Add($"{de}’i Dev Completed");
                return string.Join(", ", parts);
            }

            void TypeBlock(string label, IEnumerable<JiraIssue> src, string prefixText)
            {
                var list = src.ToList();
                if (list.Count == 0) return;

                int op = list.Count(i => IsOpenStatus(i.Status));
                int cl = list.Count(i => IsClosedStatus(i.Status));
                int de = list.Count(i => IsDevCompletedStatus(i.Status));
                int cn = list.Count(i => IsCancelledStatus(i.Status));
                int ip = list.Count(i => IsInProgressStatus(i.Status));
                int iq = list.Count(i => IsInQaStatus(i.Status));
                int bl = list.Count(i => IsBlockedStatus(i.Status));

                var line = $"{prefixText} {list.Count} adet {label} kaydı açılmıştır.";
                var b = BreakdownTr(cl, cn, ip, iq, bl, op, de);
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

            body.AppendChild(BlankLine()); // spacing after summary block
        }

        // ===== Table builders =====
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
                cell.TableCellProperties ??= new TableCellProperties();
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
                    Fill = ColorGrayHeader,
                    Val = ShadingPatternValues.Clear,
                    Color = "auto"
                });

            return new TableCell(p) { TableCellProperties = cellProps };
        }

        private static TableCell MakeCell(string text)
        {
            var p = new Paragraph(new Run(new Text(text ?? string.Empty)));
            return new TableCell(p)
            {
                TableCellProperties = new TableCellProperties(
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center })
            };
        }

        private static TableCell MakeStatusCell(string status)
        {
            var fill = GetStatusFill(status);

            var p = new Paragraph(new Run(new Text(status ?? string.Empty)));
            var cell = new TableCell(p)
            {
                TableCellProperties = new TableCellProperties(
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center },
                    new Shading
                    {
                        Fill = fill,
                        Val = ShadingPatternValues.Clear,
                        Color = "auto"
                    })
            };

            return cell;
        }

        // ===== Status helpers & colors =====
        private const string ColorGreenClosed = "92D050"; // Closed
        private const string ColorBlueCancel = "1E90FF"; // Cancelled/FPA
        private const string ColorYellowOpen = "FFFF00"; // Others
        private const string ColorGrayBlocked = "C9C9C9"; // Blocked cells
        private const string ColorGrayHeader = "A6A6A6"; // Header shade

        private static bool IsOpenStatus(string? s) => !string.IsNullOrWhiteSpace(s) &&
            (s!.Trim().Equals("Open", StringComparison.OrdinalIgnoreCase));
        private static bool IsClosedStatus(string? s) => !string.IsNullOrWhiteSpace(s) &&
            (s!.Trim().Equals("Closed", StringComparison.OrdinalIgnoreCase) ||
             s.Trim().Equals("Done", StringComparison.OrdinalIgnoreCase));

        private static bool IsCancelledStatus(string? s) => !string.IsNullOrWhiteSpace(s) &&
            (s!.Trim().Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ||
             s.Trim().Equals("Canceled", StringComparison.OrdinalIgnoreCase) ||
             s.Trim().Equals("False Positive Approval", StringComparison.OrdinalIgnoreCase));

        private static bool IsInProgressStatus(string? s) => !string.IsNullOrWhiteSpace(s) &&
            (s!.Trim().Equals("In Progress", StringComparison.OrdinalIgnoreCase));

        private static bool IsDevCompletedStatus(string? s) => !string.IsNullOrWhiteSpace(s) &&
            (s!.Trim().Equals("Dev Completed", StringComparison.OrdinalIgnoreCase));

        private static bool IsInQaStatus(string? s) => !string.IsNullOrWhiteSpace(s) &&
            (s!.Trim().Equals("In Q&A", StringComparison.OrdinalIgnoreCase) ||
             s.Trim().Equals("In QA", StringComparison.OrdinalIgnoreCase));

        private static bool IsBlockedStatus(string? s) =>
            !string.IsNullOrWhiteSpace(s) && s!.Trim().Equals("Blocked", StringComparison.OrdinalIgnoreCase);

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
