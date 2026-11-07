using System.Text.RegularExpressions;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SprintReportGenerator.Models;

namespace SprintReportGenerator.Services
{
    /// <summary>
    /// Generates the report from a DOCX template:
    /// - replaces tokens in body, headers and footers
    /// - appends a Turkish summary section
    /// - appends the issue tables (all issues, Bugs, Improvements, not-tested)
    /// </summary>
    public sealed class OpenXmlTemplateProcessor
    {
        // ===== Colors =====
        private const string ColorGreenClosed = "92D050";
        private const string ColorBlueCancel = "1E90FF";
        private const string ColorYellowOpen = "FFFF00";
        private const string ColorGrayBlocked = "C9C9C9";
        private const string ColorGrayHeader = "A6A6A6";

        // Relative column widths: Project, Issue Type, Key, Summary, Status
        private static readonly string[] ColumnHeaders = { "Project", "Issue Type", "Key", "Summary", "Status" };
        private static readonly int[] ColumnWeights = { 15, 16, 13, 41, 15 };

        private const int DefaultTextWidthTwips = 8640; // Letter page with 1.25" margins

        public string Generate(string templatePath, TemplateData data, IReadOnlyList<JiraIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
                throw new FileNotFoundException("Template not found.", templatePath);

            string output = BuildOutputPath(templatePath, data);
            File.Copy(templatePath, output, overwrite: true);

            var items = (issues ?? Array.Empty<JiraIssue>()).Where(IssueTypes.IsReportType).ToList();

            using (var doc = WordprocessingDocument.Open(output, true))
            {
                var mainPart = doc.MainDocumentPart ?? throw new InvalidDataException("Template has no main document part.");
                var body = mainPart.Document.Body ??= new Body();

                EnableUpdateFieldsOnOpen(mainPart);

                var tokens = new Dictionary<string, string>
                {
                    ["{{PROJECT}}"] = data.ProjectName,
                    ["{{FORM_DATE}}"] = data.ReportDate,
                    ["{{MEMBER_NAME}}"] = data.MemberName,
                    ["{{SPRINT}}"] = SprintNumber(data.SprintName),
                };
                foreach (var root in TokenRoots(mainPart))
                    foreach (var (token, value) in tokens)
                        ReplaceToken(root, token, value);

                // Older templates have an empty cell next to "Proje Adı" instead of {{PROJECT}}
                FillEmptyCellAfterLabel(body, "Proje Adı", data.ProjectName);

                var writer = new BodyWriter(body);

                // Keep the cover / TOC alone on the first page
                writer.Add(new Paragraph(new Run(new Break { Type = BreakValues.Page })));

                AppendSummarySection(writer, data, items);

                AppendTableSection(writer, "Test Senaryo durumları", items);
                AppendTableSection(writer, "Sprint Kapsamında Açılan Buglar",
                    items.Where(i => IssueTypes.Is(i, IssueTypes.Bug)));
                AppendTableSection(writer, "Sprint Kapsamında Açılan Improvement Kayıtları",
                    items.Where(i => IssueTypes.Is(i, IssueTypes.Improvement)));
                AppendTableSection(writer, "Sprint Kapsamında Testi Tamamlanamayan Kayıtlar",
                    items.Where(i => IssueStatus.Is(i, StatusCategory.InQa)));

                mainPart.Document.Save();
            }

            return output;
        }

        // ===== Output path =====

        private static string BuildOutputPath(string templatePath, TemplateData data)
        {
            string outDir = Path.GetDirectoryName(templatePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string sprint = SprintNumber(data.SprintName);
            if (sprint.Length == 0) sprint = string.IsNullOrWhiteSpace(data.SprintName) ? "Sprint" : data.SprintName;
            string project = string.IsNullOrWhiteSpace(data.ProjectName) ? "Project" : data.ProjectName;

            return Path.Combine(outDir, $"Sprint {SafeFileName(sprint)} {SafeFileName(project)} Test Kapanış Raporu.docx");
        }

        private static string SafeFileName(string s)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private static string SprintNumber(string? sprintName) => Regex.Match(sprintName ?? string.Empty, @"\d+").Value;

        // ===== Token replacement =====

        private static IEnumerable<OpenXmlElement> TokenRoots(MainDocumentPart part)
        {
            if (part.Document.Body != null) yield return part.Document.Body;
            foreach (var h in part.HeaderParts) if (h.Header != null) yield return h.Header;
            foreach (var f in part.FooterParts) if (f.Footer != null) yield return f.Footer;
        }

        /// <summary>
        /// Replaces a token even if Word split it across several runs. Only the characters of the
        /// token are touched; the remaining runs, their formatting, tabs, breaks and fields stay intact.
        /// </summary>
        private static void ReplaceToken(OpenXmlElement root, string token, string? value)
        {
            value = SanitizeXmlText(value);

            foreach (var paragraph in root.Descendants<Paragraph>().ToList())
            {
                int searchFrom = 0;
                while (true)
                {
                    var texts = paragraph.Descendants<Text>().ToList();
                    var full = string.Concat(texts.Select(t => t.Text));
                    int start = full.IndexOf(token, searchFrom, StringComparison.Ordinal);
                    if (start < 0) break;

                    int end = start + token.Length;
                    int pos = 0;
                    bool inserted = false;

                    foreach (var t in texts)
                    {
                        int tStart = pos, tEnd = pos + t.Text.Length;
                        pos = tEnd;
                        if (tEnd <= start || tStart >= end) continue;

                        int cutFrom = Math.Max(start, tStart) - tStart;
                        int cutTo = Math.Min(end, tEnd) - tStart;
                        t.Text = t.Text[..cutFrom] + (inserted ? string.Empty : value) + t.Text[cutTo..];
                        t.Space = SpaceProcessingModeValues.Preserve;
                        inserted = true;
                    }

                    searchFrom = start + value.Length;
                }
            }
        }

        private static void FillEmptyCellAfterLabel(Body body, string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            foreach (var labelCell in body.Descendants<TableCell>().ToList())
            {
                if (!string.Equals(labelCell.InnerText.Trim(), label, StringComparison.OrdinalIgnoreCase)) continue;
                if (labelCell.NextSibling<TableCell>() is not { } target || target.InnerText.Trim().Length > 0) continue;

                var paragraph = target.Elements<Paragraph>().FirstOrDefault() ?? target.AppendChild(new Paragraph());
                var run = new Run(new Text(SanitizeXmlText(value)) { Space = SpaceProcessingModeValues.Preserve });

                // Same font size as the label, but not bold
                if (labelCell.Descendants<RunProperties>().FirstOrDefault()?.CloneNode(true) is RunProperties rp)
                {
                    rp.Bold?.Remove();
                    run.RunProperties = rp;
                }
                paragraph.AppendChild(run);
            }
        }

        // ===== Summary (TR) =====

        private static void AppendSummarySection(BodyWriter writer, TemplateData data, List<JiraIssue> items)
        {
            writer.Add(MakeHeading("Özet"));
            writer.Add(BlankLine());

            var dateLead = BuildDateLead(data.SprintStartDate, data.SprintEndDate);
            var project = string.IsNullOrWhiteSpace(data.ProjectName) ? "Proje" : data.ProjectName;
            var sprint = string.IsNullOrWhiteSpace(data.SprintName) ? "Sprint" : data.SprintName;
            int closed = items.Count(i => IssueStatus.Is(i, StatusCategory.Closed));

            const string env = "Proven Test ve Pre-production";
            var prefix = dateLead.Length == 0 ? $"{env} ortamında koşulan " : $"{dateLead} {env} ortamında koşulan ";
            var lead = $"{prefix}{project} projesi {sprint} sprintine ait {items.Count} kayıt bulunmaktadır. " +
                       $"Bunlardan {TurkishText.Possessive(closed)} Closed durumundadır.";

            var leadParagraph = MakeTextParagraph(lead);
            leadParagraph.ParagraphProperties = new ParagraphProperties(new SpacingBetweenLines { After = "240" });
            writer.Add(leadParagraph);

            writer.Add(MakeHeading("Test genel, hata bildirimi ve iyileştirme durumu"));
            writer.Add(BlankLine());

            AppendTypeBullet(writer, "Bug", items.Where(i => IssueTypes.Is(i, IssueTypes.Bug)));
            AppendTypeBullet(writer, "İyileştirme talebi", items.Where(i => IssueTypes.Is(i, IssueTypes.Improvement)));
            AppendTypeBullet(writer, "Story", items.Where(i => IssueTypes.Is(i, IssueTypes.Story)));

            int inQa = items.Count(i => IssueStatus.Is(i, StatusCategory.InQa));
            writer.Add(MakeBullet(inQa == 0
                ? "Sprint kapsamında testi tamamlanamayan kayıt bulunmamaktadır."
                : $"Sprint kapsamında testi tamamlanamayan {inQa} adet kayıt bulunmaktadır."));

            writer.Add(BlankLine());
        }

        private static string BuildDateLead(string start, string end)
        {
            bool hasStart = !string.IsNullOrWhiteSpace(start);
            bool hasEnd = !string.IsNullOrWhiteSpace(end);

            if (hasStart && hasEnd) return $"{start} – {end} tarihleri arasında";
            if (hasStart) return $"{start} tarihinden itibaren";
            if (hasEnd) return $"{end} tarihine kadar";
            return string.Empty;
        }

        // Order of the status breakdown in the summary bullets
        private static readonly (StatusCategory Category, string Label)[] BreakdownOrder =
        {
            (StatusCategory.Closed, "Closed"),
            (StatusCategory.Cancelled, "Cancelled"),
            (StatusCategory.InProgress, "In Progress"),
            (StatusCategory.InQa, "In Q&A"),
            (StatusCategory.Blocked, "Blocked"),
            (StatusCategory.Open, "Open"),
            (StatusCategory.DevCompleted, "Dev Completed"),
        };

        private static void AppendTypeBullet(BodyWriter writer, string label, IEnumerable<JiraIssue> source)
        {
            var list = source.ToList();
            if (list.Count == 0)
            {
                writer.Add(MakeBullet($"Sprint kapsamında {label} kaydı bulunmamaktadır."));
                return;
            }

            var parts = BreakdownOrder
                .Select(b => (b.Label, Count: list.Count(i => IssueStatus.Is(i, b.Category))))
                // statuses that are not mapped are listed with their own Jira name instead of being dropped
                .Concat(list.Where(i => IssueStatus.Is(i, StatusCategory.Other))
                            .GroupBy(i => string.IsNullOrWhiteSpace(i.Status) ? "Bilinmeyen" : i.Status.Trim(),
                                     StringComparer.OrdinalIgnoreCase)
                            .Select(g => (Label: g.Key, Count: g.Count())))
                .Where(p => p.Count > 0)
                .Select(p => $"{TurkishText.Possessive(p.Count)} {p.Label}");

            var line = $"Testler sırasında {list.Count} adet {label} kaydı açılmıştır. {string.Join(", ", parts)} durumundadır.";
            writer.Add(MakeBullet(line));
        }

        // ===== Tables =====

        private static void AppendTableSection(BodyWriter writer, string heading, IEnumerable<JiraIssue> source)
        {
            writer.Add(MakeHeading(heading));
            writer.Add(BlankLine());
            writer.Add(BuildIssuesTable(source.ToList(), writer.TextWidthTwips));
            writer.Add(BlankLine());
        }

        private static Table BuildIssuesTable(IReadOnlyList<JiraIssue> items, int totalWidth)
        {
            // A table without w:tblGrid / cell widths is invalid OOXML; Word and LibreOffice then
            // collapse the columns, so every column gets an explicit width.
            int weightSum = ColumnWeights.Sum();
            var widths = ColumnWeights.Select(w => totalWidth * w / weightSum).ToArray();
            widths[^1] += totalWidth - widths.Sum();

            var table = new Table(
                new TableProperties
                {
                    TableWidth = new TableWidth { Type = TableWidthUnitValues.Dxa, Width = totalWidth.ToString() },
                    TableBorders = new TableBorders(
                        new TopBorder { Val = BorderValues.Single, Size = 6 },
                        new LeftBorder { Val = BorderValues.Single, Size = 6 },
                        new BottomBorder { Val = BorderValues.Single, Size = 6 },
                        new RightBorder { Val = BorderValues.Single, Size = 6 },
                        new InsideHorizontalBorder { Val = BorderValues.Single, Size = 6 },
                        new InsideVerticalBorder { Val = BorderValues.Single, Size = 6 }),
                    TableLayout = new TableLayout { Type = TableLayoutValues.Fixed }
                },
                new TableGrid(widths.Select(w => new GridColumn { Width = w.ToString() })));

            // Header row (repeated on every page)
            var header = new TableRow(new TableRowProperties(new TableHeader()));
            for (int c = 0; c < ColumnHeaders.Length; c++)
                header.Append(MakeCell(ColumnHeaders[c], widths[c], ColorGrayHeader, bold: true));
            table.Append(header);

            if (items.Count == 0)
            {
                table.Append(new TableRow(MakeCell("(No issues)", totalWidth, gridSpan: ColumnHeaders.Length)));
                return table;
            }

            foreach (var i in items)
            {
                table.Append(new TableRow(
                    MakeCell(i.Project, widths[0]),
                    MakeCell(i.Type, widths[1]),
                    MakeCell(i.Key, widths[2]),
                    MakeCell(i.Summary, widths[3]),
                    MakeCell(i.Status, widths[4], GetStatusFill(i.Status))));
            }

            return table;
        }

        private static TableCell MakeCell(string? text, int width, string? fill = null, bool bold = false, int gridSpan = 1)
        {
            // Object initializer => child elements are written in schema order (tcW, gridSpan, shd, vAlign)
            var props = new TableCellProperties
            {
                TableCellWidth = new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = width.ToString() },
                TableCellVerticalAlignment = new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }
            };
            if (gridSpan > 1) props.GridSpan = new GridSpan { Val = gridSpan };
            if (fill != null) props.Shading = new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fill };

            var run = new Run(new Text(SanitizeXmlText(text)) { Space = SpaceProcessingModeValues.Preserve });
            if (bold) run.RunProperties = new RunProperties(new Bold());

            return new TableCell(props, new Paragraph(run));
        }

        private static string GetStatusFill(string? status) => IssueStatus.Classify(status) switch
        {
            StatusCategory.Closed => ColorGreenClosed,
            StatusCategory.Cancelled => ColorBlueCancel,
            StatusCategory.Blocked => ColorGrayBlocked,
            _ => ColorYellowOpen
        };

        // ===== Paragraph builders =====

        private static Paragraph BlankLine() => new Paragraph();

        private static Paragraph MakeTextParagraph(string text) =>
            new Paragraph(new Run(new Text(SanitizeXmlText(text)) { Space = SpaceProcessingModeValues.Preserve }));

        private static Paragraph MakeHeading(string text, int level = 2)
        {
            level = Math.Clamp(level, 1, 9);
            var p = MakeTextParagraph(text);
            p.GetFirstChild<Run>()!.RunProperties = new RunProperties(new Bold());
            p.ParagraphProperties = new ParagraphProperties
            {
                ParagraphStyleId = new ParagraphStyleId { Val = $"Heading{level}" },
                SpacingBetweenLines = new SpacingBetweenLines { After = "240" },
                OutlineLevel = new OutlineLevel { Val = level - 1 }
            };
            return p;
        }

        private static Paragraph MakeBullet(string text)
        {
            var p = MakeTextParagraph("• " + text);
            p.ParagraphProperties = new ParagraphProperties
            {
                SpacingBetweenLines = new SpacingBetweenLines { Before = "120", After = "120" },
                Indentation = new Indentation { Left = "720", Hanging = "360" }
            };
            return p;
        }

        private static void EnableUpdateFieldsOnOpen(MainDocumentPart mainPart)
        {
            var part = mainPart.DocumentSettingsPart ?? mainPart.AddNewPart<DocumentSettingsPart>();
            part.Settings ??= new Settings();

            var upd = part.Settings.Elements<UpdateFieldsOnOpen>().FirstOrDefault();
            if (upd == null) part.Settings.AppendChild(new UpdateFieldsOnOpen { Val = true });
            else upd.Val = true;

            part.Settings.Save();
        }

        /// <summary>Drops characters that are illegal in XML (Jira summaries can contain them) and flattens newlines.</summary>
        private static string SanitizeXmlText(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var flat = s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            return new string(flat.Where(c => XmlConvert.IsXmlChar(c) || char.IsSurrogate(c)).ToArray());
        }

        /// <summary>
        /// Appends content to the body but always before the final w:sectPr
        /// (content after the last section properties is invalid OOXML).
        /// </summary>
        private sealed class BodyWriter
        {
            private readonly Body _body;
            private readonly SectionProperties? _finalSection;

            public BodyWriter(Body body)
            {
                _body = body;
                _finalSection = body.Elements<SectionProperties>().LastOrDefault();
                TextWidthTwips = ComputeTextWidth(_finalSection);
            }

            public int TextWidthTwips { get; }

            public void Add(OpenXmlElement element)
            {
                if (_finalSection != null) _body.InsertBefore(element, _finalSection);
                else _body.AppendChild(element);
            }

            private static int ComputeTextWidth(SectionProperties? sect)
            {
                var size = sect?.GetFirstChild<PageSize>();
                var margin = sect?.GetFirstChild<PageMargin>();
                if (size?.Width?.Value is not uint pageWidth || margin == null) return DefaultTextWidthTwips;

                long width = (long)pageWidth
                             - (margin.Left?.Value ?? 0u)
                             - (margin.Right?.Value ?? 0u)
                             - (margin.Gutter?.Value ?? 0u);
                return width > 1000 ? (int)width : DefaultTextWidthTwips;
            }
        }
    }

    internal static class TurkishText
    {
        /// <summary>
        /// Number with the Turkish possessive suffix after an apostrophe: 1’i, 2’si, 3’ü, 6’sı, 9’u, 10’u, 40’ı, 100’ü…
        /// </summary>
        public static string Possessive(int n)
        {
            static string Units(int d) => d switch
            {
                1 => "i", 2 => "si", 3 => "ü", 4 => "ü", 5 => "i",
                6 => "sı", 7 => "si", 8 => "i", _ => "u" // 9
            };
            static string Tens(int d) => d switch
            {
                1 => "u", 2 => "si", 3 => "u", 4 => "ı", 5 => "si",
                6 => "ı", 7 => "i", 8 => "i", _ => "ı" // 90
            };

            int a = Math.Abs(n);
            string suffix =
                a == 0 ? "ı" :
                a % 10 != 0 ? Units(a % 10) :
                a % 100 != 0 ? Tens(a / 10 % 10) :
                a % 1000 != 0 ? "ü" :
                a % 1_000_000 != 0 ? "i" : "u";

            return $"{n}’{suffix}";
        }
    }
}
