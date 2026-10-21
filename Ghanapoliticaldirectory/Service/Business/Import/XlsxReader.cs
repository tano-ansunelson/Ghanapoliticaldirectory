using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Ghanapoliticaldirectory.Service.Business.Import;


    public sealed record SheetData(string Name, IReadOnlyList<SheetRow> Rows);

    public sealed record SheetRow(int Number, IReadOnlyList<string> Cells);

    public sealed class XlsxFormatException : Exception
    {
        public XlsxFormatException(string message) : base(message) { }
    }

public static class XlsxReader
    {

    public const int MaxEntryBytes = 40 * 1024 * 1024;   // uncompressed size of any one part: stops zip bombs
    public const int MaxRows = 5000;
    public const int MaxColumns = 40;

    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static SheetData Read(Stream file, string? preferredSheet = null)
    {
        ZipArchive zip;
        try { zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException) { throw new XlsxFormatException("This is not an Excel (.xlsx) file. Save it from Excel as \"Excel Workbook (*.xlsx)\"."); }

        using (zip)
        {
            var workbook = Load(zip, "xl/workbook.xml") ?? throw new XlsxFormatException("This is not an Excel (.xlsx) file: the workbook part is missing.");
            var sheets = workbook.Root!.Element(Main + "sheets")?.Elements(Main + "sheet").ToList() ?? new();
            if (sheets.Count == 0) throw new XlsxFormatException("The workbook has no worksheets.");

            var chosen = (preferredSheet is null ? null
                : sheets.FirstOrDefault(s => string.Equals((string?)s.Attribute("name"), preferredSheet, StringComparison.OrdinalIgnoreCase))) ?? sheets[0];
            var name = (string?)chosen.Attribute("name") ?? "Sheet1";
            var rid = (string?)chosen.Attribute(Rel + "id");

            var rels = Load(zip, "xl/_rels/workbook.xml.rels");
            var target = rels?.Root?.Elements(PkgRel + "Relationship").FirstOrDefault(r => (string?)r.Attribute("Id") == rid)?.Attribute("Target")?.Value;
            if (target is null) throw new XlsxFormatException("The worksheet could not be located inside the file.");
            var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;

            var shared = ReadSharedStrings(zip);
            var sheetDoc = Load(zip, path) ?? throw new XlsxFormatException("The worksheet could not be read.");
            return new SheetData(name, ReadRows(sheetDoc, shared));
        }
    }

    private static List<SheetRow> ReadRows(XDocument doc, IReadOnlyList<string> shared)
    {
        var rows = new List<SheetRow>();
        var data = doc.Root!.Element(Main + "sheetData");
        if (data is null) return rows;

        var nextRow = 1;
        foreach (var r in data.Elements(Main + "row"))
        {
            var number = int.TryParse((string?)r.Attribute("r"), out var n) ? n : nextRow;
            nextRow = number + 1;
            if (rows.Count >= MaxRows) throw new XlsxFormatException($"The sheet has more than {MaxRows} rows. Split the file into smaller files.");

            var cells = new List<string>();
            var nextCol = 0;
            foreach (var c in r.Elements(Main + "c"))
            {
                var col = ColumnIndex((string?)c.Attribute("r")) ?? nextCol;
                if (col >= MaxColumns) { nextCol = col + 1; continue; }
                while (cells.Count < col) cells.Add("");
                cells.Add(CellText(c, shared));
                nextCol = col + 1;
            }
            while (cells.Count > 0 && cells[^1].Length == 0) cells.RemoveAt(cells.Count - 1);
            if (cells.Count > 0) rows.Add(new SheetRow(number, cells));
        }
        return rows;
    }

    private static string CellText(XElement c, IReadOnlyList<string> shared)
    {
        var type = (string?)c.Attribute("t");
        if (type == "inlineStr") return Collapse(string.Concat(c.Element(Main + "is")?.Descendants(Main + "t").Select(t => t.Value) ?? Array.Empty<string>()));
        var v = c.Element(Main + "v")?.Value;
        if (v is null) return "";
        switch (type)
        {
            case "s": return int.TryParse(v, out var i) && i >= 0 && i < shared.Count ? shared[i] : "";
            case "b": return v == "1" ? "TRUE" : "FALSE";
            case "e": return "";                    // an Excel error such as #N/A: treated as empty
            default: return Collapse(v);            // numbers, dates (serials), formula results, plain strings
        }
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var doc = Load(zip, "xl/sharedStrings.xml");
        if (doc is null) return list;
        foreach (var si in doc.Root!.Elements(Main + "si"))
            list.Add(Collapse(string.Concat(si.Descendants(Main + "t").Select(t => t.Value))));   // joins rich-text runs
        return list;
    }

    private static string Collapse(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();

    /// <summary>"C12" gives 2 (zero-based column).</summary>
    internal static int? ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        var col = 0; var any = false;
        foreach (var ch in reference)
        {
            if (ch is >= 'A' and <= 'Z') { col = col * 26 + (ch - 'A' + 1); any = true; }
            else if (ch is >= 'a' and <= 'z') { col = col * 26 + (ch - 'a' + 1); any = true; }
            else break;
        }
        return any ? col - 1 : null;
    }

    private static XDocument? Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null) return null;
        if (entry.Length > MaxEntryBytes) throw new XlsxFormatException("The file is too large to import.");
        // The declared size can be forged, so the bytes are counted as they are read.
        using var raw = entry.Open();
        using var s = new MemoryStream();
        var buffer = new byte[81920]; int read; long total = 0;
        while ((read = raw.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxEntryBytes) throw new XlsxFormatException("The file is too large to import.");
            s.Write(buffer, 0, read);
        }
        s.Position = 0;
        // Doctype/DTD processing is switched off so a crafted file cannot pull in other files or expand entities.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0 };
        using var reader = XmlReader.Create(s, settings);
        try { return XDocument.Load(reader); }
        catch (XmlException) { throw new XlsxFormatException("The file is damaged or is not a valid Excel workbook."); }
    }
}

