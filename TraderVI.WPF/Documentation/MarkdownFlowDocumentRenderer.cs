#nullable enable

using Core.Documentation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace TraderVI.WPF.Documentation;

public sealed record MarkdownRenderResult(
    FlowDocument Document,
    IReadOnlyDictionary<string, FrameworkContentElement> Headings);

public sealed class MarkdownFlowDocumentRenderer
{
    private static readonly Regex HeadingPattern = new(
        @"^\s{0,3}(?<marks>#{1,6})\s+(?<text>.+?)\s*#*\s*$",
        RegexOptions.Compiled);
    private static readonly Regex FencePattern = new(
        @"^\s*(?<fence>`{3,}|~{3,})\s*(?<language>.*)$",
        RegexOptions.Compiled);
    private static readonly Regex ListPattern = new(
        @"^\s*(?<marker>[-+*]|\d+\.)\s+(?<text>.+)$",
        RegexOptions.Compiled);
    private static readonly Regex HorizontalRulePattern = new(
        @"^\s{0,3}((\*\s*){3,}|(-\s*){3,}|(_\s*){3,})$",
        RegexOptions.Compiled);

    private static readonly Brush TextBrush = BrushFrom("#EAF0F8");
    private static readonly Brush MutedBrush = BrushFrom("#9BA8BA");
    private static readonly Brush AccentBrush = BrushFrom("#76B8FF");
    private static readonly Brush PanelBrush = BrushFrom("#171D25");
    private static readonly Brush BorderBrush = BrushFrom("#344154");
    private static readonly Brush QuoteBrush = BrushFrom("#202B38");

    public MarkdownRenderResult Render(ProjectMarkdownDocument source)
    {
        FlowDocument document = new()
        {
            Background = Brushes.Transparent,
            Foreground = TextBrush,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 15,
            LineHeight = 23,
            PagePadding = new Thickness(32, 24, 38, 48),
            ColumnWidth = double.PositiveInfinity
        };
        Dictionary<string, FrameworkContentElement> headings =
            new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> headingCounts =
            new(StringComparer.OrdinalIgnoreCase);
        string[] lines = source.Content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        int index = 0;
        while (index < lines.Length)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                index++;
                continue;
            }

            Match fence = FencePattern.Match(lines[index]);
            if (fence.Success)
            {
                index = AddCodeBlock(document, lines, index, fence);
                continue;
            }

            Match heading = HeadingPattern.Match(lines[index]);
            if (heading.Success)
            {
                AddHeading(document, headings, headingCounts,
                    heading.Groups["text"].Value,
                    heading.Groups["marks"].Value.Length);
                index++;
                continue;
            }

            if (HorizontalRulePattern.IsMatch(lines[index]))
            {
                AddHorizontalRule(document);
                index++;
                continue;
            }

            if (IsTableStart(lines, index))
            {
                index = AddTable(document, lines, index);
                continue;
            }

            if (lines[index].TrimStart().StartsWith('>'))
            {
                index = AddBlockquote(document, lines, index);
                continue;
            }

            Match list = ListPattern.Match(lines[index]);
            if (list.Success)
            {
                index = AddList(document, lines, index, list.Groups["marker"].Value);
                continue;
            }

            index = AddParagraph(document, lines, index);
        }

        return new MarkdownRenderResult(document, headings);
    }

    private static int AddCodeBlock(
        FlowDocument document,
        string[] lines,
        int index,
        Match opening)
    {
        string marker = opening.Groups["fence"].Value;
        string language = opening.Groups["language"].Value.Trim();
        StringBuilder code = new();
        index++;
        while (index < lines.Length &&
               !lines[index].TrimStart().StartsWith(marker, StringComparison.Ordinal))
        {
            if (code.Length > 0)
                code.AppendLine();
            code.Append(lines[index]);
            index++;
        }
        if (index < lines.Length)
            index++;

        Paragraph paragraph = new(new Run(code.ToString()))
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            LineHeight = 20,
            Background = PanelBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 8, 0, 16)
        };
        if (language.Length > 0)
            paragraph.ToolTip = language;
        document.Blocks.Add(paragraph);
        return index;
    }

    private static void AddHeading(
        FlowDocument document,
        IDictionary<string, FrameworkContentElement> headings,
        IDictionary<string, int> headingCounts,
        string text,
        int level)
    {
        Paragraph paragraph = new()
        {
            FontSize = level switch { 1 => 30, 2 => 24, 3 => 20, 4 => 17, _ => 15 },
            FontWeight = level <= 3 ? FontWeights.SemiBold : FontWeights.Bold,
            Foreground = level == 1 ? Brushes.White : TextBrush,
            Margin = new Thickness(0, level == 1 ? 4 : 24, 0, level <= 2 ? 10 : 6),
            KeepWithNext = true
        };
        AddInlines(paragraph.Inlines, text);
        document.Blocks.Add(paragraph);

        string baseId = MarkdownHeadingIds.Create(text);
        if (baseId.Length == 0)
            return;
        headingCounts.TryGetValue(baseId, out int count);
        string id = count == 0 ? baseId : $"{baseId}-{count}";
        headingCounts[baseId] = count + 1;
        headings[id] = paragraph;
    }

    private static void AddHorizontalRule(FlowDocument document)
    {
        Border line = new()
        {
            Height = 1,
            Background = BorderBrush,
            Margin = new Thickness(0, 14, 0, 16)
        };
        document.Blocks.Add(new BlockUIContainer(line));
    }

    private static bool IsTableStart(string[] lines, int index)
    {
        if (index + 1 >= lines.Length || !lines[index].Contains('|'))
            return false;
        string[] separators = SplitTableRow(lines[index + 1]);
        return separators.Length > 0 && separators.All(cell =>
            Regex.IsMatch(cell.Trim(), @"^:?-{3,}:?$"));
    }

    private static int AddTable(FlowDocument document, string[] lines, int index)
    {
        string[] headers = SplitTableRow(lines[index]);
        Table table = new()
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 8, 0, 18),
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1)
        };
        for (int column = 0; column < headers.Length; column++)
            table.Columns.Add(new TableColumn());
        TableRowGroup group = new();
        table.RowGroups.Add(group);
        group.Rows.Add(CreateTableRow(headers, true));
        index += 2;
        while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]) &&
               lines[index].Contains('|'))
        {
            group.Rows.Add(CreateTableRow(SplitTableRow(lines[index]), false, headers.Length));
            index++;
        }
        document.Blocks.Add(table);
        return index;
    }

    private static TableRow CreateTableRow(string[] cells, bool isHeader, int? width = null)
    {
        int count = width ?? cells.Length;
        TableRow row = new() { Background = isHeader ? QuoteBrush : Brushes.Transparent };
        for (int index = 0; index < count; index++)
        {
            Paragraph content = new() { Margin = new Thickness(0) };
            AddInlines(content.Inlines, index < cells.Length ? cells[index].Trim() : string.Empty);
            if (isHeader)
                content.FontWeight = FontWeights.SemiBold;
            row.Cells.Add(new TableCell(content)
            {
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(10, 7, 10, 7)
            });
        }
        return row;
    }

    private static string[] SplitTableRow(string line)
    {
        string value = line.Trim();
        if (value.StartsWith('|'))
            value = value[1..];
        if (value.EndsWith('|'))
            value = value[..^1];
        return value.Split('|');
    }

    private static int AddBlockquote(FlowDocument document, string[] lines, int index)
    {
        List<string> quoteLines = [];
        while (index < lines.Length && lines[index].TrimStart().StartsWith('>'))
        {
            string line = lines[index].TrimStart()[1..];
            quoteLines.Add(line.StartsWith(' ') ? line[1..] : line);
            index++;
        }
        Paragraph paragraph = new()
        {
            Background = QuoteBrush,
            BorderBrush = AccentBrush,
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 8, 0, 16),
            Foreground = MutedBrush,
            FontStyle = FontStyles.Italic
        };
        AddInlines(paragraph.Inlines, string.Join(" ", quoteLines));
        document.Blocks.Add(paragraph);
        return index;
    }

    private static int AddList(
        FlowDocument document,
        string[] lines,
        int index,
        string firstMarker)
    {
        bool ordered = char.IsDigit(firstMarker[0]);
        Match firstItem = ListPattern.Match(lines[index]);
        bool checklist = firstItem.Success &&
            (firstItem.Groups["text"].Value.StartsWith("[ ] ", StringComparison.Ordinal) ||
             firstItem.Groups["text"].Value.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase));
        System.Windows.Documents.List list = new()
        {
            MarkerStyle = checklist
                ? TextMarkerStyle.None
                : ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(18, 5, 0, 14),
            Padding = new Thickness(12, 0, 0, 0)
        };
        while (index < lines.Length)
        {
            Match item = ListPattern.Match(lines[index]);
            if (!item.Success || char.IsDigit(item.Groups["marker"].Value[0]) != ordered)
                break;
            string text = item.Groups["text"].Value;
            bool isChecklist = text.StartsWith("[ ] ", StringComparison.Ordinal) ||
                               text.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase);
            Paragraph paragraph = new() { Margin = new Thickness(0, 2, 0, 2) };
            if (isChecklist)
            {
                bool isChecked = text[1] is 'x' or 'X';
                paragraph.Inlines.Add(new Run(isChecked ? "☑  " : "☐  ")
                {
                    Foreground = isChecked ? BrushFrom("#55D68B") : MutedBrush
                });
                text = text[4..];
            }
            AddInlines(paragraph.Inlines, text);
            list.ListItems.Add(new ListItem(paragraph));
            index++;
        }
        document.Blocks.Add(list);
        return index;
    }

    private static int AddParagraph(FlowDocument document, string[] lines, int index)
    {
        StringBuilder text = new();
        while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
        {
            if (text.Length > 0 && IsBlockStart(lines, index))
                break;
            if (text.Length > 0)
                text.Append(' ');
            text.Append(lines[index].Trim());
            index++;
        }
        Paragraph paragraph = new() { Margin = new Thickness(0, 0, 0, 13) };
        AddInlines(paragraph.Inlines, text.ToString());
        document.Blocks.Add(paragraph);
        return index;
    }

    private static bool IsBlockStart(string[] lines, int index) =>
        FencePattern.IsMatch(lines[index]) ||
        HeadingPattern.IsMatch(lines[index]) ||
        HorizontalRulePattern.IsMatch(lines[index]) ||
        ListPattern.IsMatch(lines[index]) ||
        lines[index].TrimStart().StartsWith('>') ||
        IsTableStart(lines, index);

    private static void AddInlines(InlineCollection inlines, string text)
    {
        int index = 0;
        while (index < text.Length)
        {
            if (text[index] == '\\' && index + 1 < text.Length)
            {
                inlines.Add(new Run(text[index + 1].ToString()));
                index += 2;
                continue;
            }

            if (text[index] == '`')
            {
                int end = text.IndexOf('`', index + 1);
                if (end > index)
                {
                    inlines.Add(new Run(text[(index + 1)..end])
                    {
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        FontSize = 13,
                        Background = PanelBrush,
                        Foreground = BrushFrom("#B9D9FF")
                    });
                    index = end + 1;
                    continue;
                }
            }

            if (text[index] == '[')
            {
                int labelEnd = text.IndexOf("](", index, StringComparison.Ordinal);
                int targetEnd = labelEnd >= 0
                    ? text.IndexOf(')', labelEnd + 2)
                    : -1;
                if (labelEnd > index && targetEnd > labelEnd)
                {
                    string label = text[(index + 1)..labelEnd];
                    string target = ExtractLinkTarget(text[(labelEnd + 2)..targetEnd]);
                    Hyperlink hyperlink = new() { Tag = target, Foreground = AccentBrush };
                    AddInlines(hyperlink.Inlines, label);
                    inlines.Add(hyperlink);
                    index = targetEnd + 1;
                    continue;
                }
            }

            string? strongMarker = text.AsSpan(index).StartsWith("**") ? "**" :
                text.AsSpan(index).StartsWith("__") ? "__" : null;
            if (strongMarker is not null)
            {
                int end = text.IndexOf(strongMarker, index + 2, StringComparison.Ordinal);
                if (end > index + 2)
                {
                    Bold bold = new();
                    AddInlines(bold.Inlines, text[(index + 2)..end]);
                    inlines.Add(bold);
                    index = end + 2;
                    continue;
                }
            }

            if (text[index] is '*' or '_')
            {
                char marker = text[index];
                int end = text.IndexOf(marker, index + 1);
                if (end > index + 1)
                {
                    Italic italic = new();
                    AddInlines(italic.Inlines, text[(index + 1)..end]);
                    inlines.Add(italic);
                    index = end + 1;
                    continue;
                }
            }

            int next = index + 1;
            while (next < text.Length && text[next] is not ('\\' or '`' or '[' or '*' or '_'))
                next++;
            inlines.Add(new Run(text[index..next]));
            index = next;
        }
    }

    private static string ExtractLinkTarget(string value)
    {
        string target = value.Trim();
        if (target.StartsWith('<'))
        {
            int closing = target.IndexOf('>');
            return closing > 1 ? target[1..closing] : target;
        }

        int whitespace = target.IndexOfAny([' ', '\t']);
        return whitespace > 0 ? target[..whitespace] : target;
    }

    private static SolidColorBrush BrushFrom(string value)
    {
        SolidColorBrush brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
        brush.Freeze();
        return brush;
    }
}
