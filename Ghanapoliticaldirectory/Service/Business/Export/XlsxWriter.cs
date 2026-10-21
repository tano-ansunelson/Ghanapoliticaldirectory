using System.IO.Compression;
using System.Security;
using System.Text;

namespace Ghanapoliticaldirectory.Service.Business.Export;


/// <summary>One sheet of an export: a title, column headings and rows of plain values.</summary>
    public sealed class ExportSheet
    {
        public ExportSheet(string name, IReadOnlyList<string> headers) { Name = name; Headers = headers; }
        public string Name { get; }
        public IReadOnlyList<string> Headers { get; }
        public List<object?[]> Rows { get; } = new();
        public void Add(params object?[] values) => Rows.Add(values);
    }
public sealed class XlsxWriter
{
    public static byte[] Write(IReadOnlyList<ExportSheet> sheets)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                string.Concat(sheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")) +
                "</Types>");
            Entry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");

            var names = UniqueNames(sheets);
            Entry(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" +
                string.Concat(names.Select((n, i) => $"<sheet name=\"{Esc(n)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>")) + "</sheets></workbook>");
            Entry(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>")) +
                $"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Entry(zip, "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE8EEF4\"/></patternFill></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/></cellXfs>" +
                "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");

            for (var i = 0; i < sheets.Count; i++) Entry(zip, $"xl/worksheets/sheet{i + 1}.xml", SheetXml(sheets[i]));
        }
        return ms.ToArray();
    }

    private static string SheetXml(ExportSheet s)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><cols>");
        for (var c = 0; c < s.Headers.Count; c++)
        {
            var longest = s.Headers[c].Length;
            foreach (var row in s.Rows.Take(200)) if (c < row.Length) longest = Math.Max(longest, Text(row[c]).Length);
            sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{Math.Min(Math.Max(longest + 2, 10), 60)}\" customWidth=\"1\"/>");
        }
        sb.Append("</cols><sheetData><row r=\"1\">");
        for (var c = 0; c < s.Headers.Count; c++) sb.Append($"<c r=\"{Ref(c, 1)}\" t=\"inlineStr\" s=\"1\"><is><t>{Esc(s.Headers[c])}</t></is></c>");
        sb.Append("</row>");
        for (var r = 0; r < s.Rows.Count; r++)
        {
            var rowNo = r + 2;
            sb.Append($"<row r=\"{rowNo}\">");
            var row = s.Rows[r];
            for (var c = 0; c < s.Headers.Count && c < row.Length; c++)
            {
                var v = row[c];
                if (v is null) continue;
                if (v is int or long or short or byte) sb.Append($"<c r=\"{Ref(c, rowNo)}\"><v>{Convert.ToInt64(v)}</v></c>");
                else
                {
                    var t = Text(v);
                    if (t.Length == 0) continue;
                    if (t.Length > 32000) t = t[..32000];
                    sb.Append($"<c r=\"{Ref(c, rowNo)}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(t)}</t></is></c>");
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");
        if (s.Headers.Count > 0) sb.Append($"<autoFilter ref=\"A1:{Ref(s.Headers.Count - 1, Math.Max(1, s.Rows.Count + 1))}\"/>");
        return sb.Append("</worksheet>").ToString();
    }



    private static string Text(object? v) => v switch
    {
        null => "",
        DateOnly d => d.ToString("yyyy-MM-dd"),
        DateTime d => d.ToString("yyyy-MM-dd"),
        bool b => b ? "Yes" : "No",
        Enum e => e.ToString(),
        _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? ""
    };

    private static string Ref(int col, int row)
    {
        var name = "";
        for (var n = col; n >= 0; n = n / 26 - 1) name = (char)('A' + n % 26) + name;
        return name + row;
    }

    /// <summary>Excel sheet names: at most 31 characters, none of []:*?/\ and each one different.</summary>
    private static List<string> UniqueNames(IReadOnlyList<ExportSheet> sheets)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var s in sheets)
        {
            var n = new string(s.Name.Where(ch => "[]:*?/\\".IndexOf(ch) < 0).ToArray());
            if (n.Length == 0) n = "Sheet";
            if (n.Length > 31) n = n[..31];
            var candidate = n; var k = 2;
            while (!used.Add(candidate)) { var suffix = " " + k++; candidate = (n.Length + suffix.Length > 31 ? n[..(31 - suffix.Length)] : n) + suffix; }
            result.Add(candidate);
        }
        return result;
    }

    private static string Esc(string s)
    {
        var clean = new string(s.Where(ch => ch == '\t' || ch == '\n' || ch == '\r' || ch >= ' ').ToArray());   // control characters are not legal in XML
        return SecurityElement.Escape(clean) ?? "";
    }

    private static void Entry(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content);
    }









}

