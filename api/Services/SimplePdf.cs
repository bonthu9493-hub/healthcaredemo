using System.Globalization;
using System.Text;

namespace HospitalApi.Services;

public record PdfLine(string Text, double Size = 10, bool Bold = false, bool Mono = true);

/// <summary>
/// A tiny PDF writer for text-only documents (receipts). It uses the PDF standard fonts, so there is nothing to
/// install and no font files to ship, and it behaves the same on Windows, Linux and in Docker.
/// Only Latin-1 text is supported; anything else is shown as "?".
/// </summary>
public static class SimplePdf
{
    private const int LinesPerPage = 52;
    private const double Left = 50, Top = 790, Leading = 14;

    public static byte[] Create(IReadOnlyList<PdfLine> lines)
    {
        var pages = lines.Chunk(LinesPerPage).ToList();
        if (pages.Count == 0) pages.Add([]);

        // Object numbers: 1 catalog, 2 page tree, 3-5 fonts, then (page, content) pairs.
        var objects = new List<byte[]>();
        void Add(string body) => objects.Add(Encoding.Latin1.GetBytes(body));

        var pageObjectNumbers = Enumerable.Range(0, pages.Count).Select(i => 6 + i * 2).ToList();
        Add("<< /Type /Catalog /Pages 2 0 R >>");
        Add($"<< /Type /Pages /Kids [{string.Join(" ", pageObjectNumbers.Select(n => $"{n} 0 R"))}] /Count {pages.Count} >>");
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");

        for (var p = 0; p < pages.Count; p++)
        {
            var content = new StringBuilder();
            var y = Top;
            foreach (var line in pages[p])
            {
                var font = line.Mono ? "F3" : line.Bold ? "F2" : "F1";
                content.Append(CultureInfo.InvariantCulture,
                    $"BT /{font} {line.Size:0.##} Tf {Left} {y:0.##} Td ({Escape(line.Text)}) Tj ET\n");
                y -= Math.Max(Leading, line.Size + 4);
            }
            var stream = Encoding.Latin1.GetBytes(content.ToString());
            var contentNumber = pageObjectNumbers[p] + 1;
            Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents {contentNumber} 0 R >>");
            objects.Add(Encoding.Latin1.GetBytes($"<< /Length {stream.Length} >>\nstream\n").Concat(stream)
                .Concat(Encoding.Latin1.GetBytes("\nendstream")).ToArray());
        }

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        Write("%PDF-1.4\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            Write($"{i + 1} 0 obj\n");
            ms.Write(objects[i]);
            Write("\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    /// <summary>Escapes the characters that are special inside a PDF string and replaces anything outside Latin-1.</summary>
    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '(' or ')' or '\\') sb.Append('\\').Append(c);
            else if (c is '\r' or '\n' or '\t') sb.Append(' ');
            else if (c < 32 || c > 255) sb.Append('?');
            else sb.Append(c);
        }
        return sb.ToString();
    }
}

public static class ReceiptPdf
{
    private static string Money(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Fit(string s, int width) => s.Length <= width ? s.PadRight(width) : s[..(width - 3)] + "...";

    public static byte[] Build(Data.Invoice invoice, string patientName, string hospitalName = "MediCare HMS")
    {
        var paid = invoice.Status == Data.InvoiceStatus.Paid;
        var number = $"INV-{invoice.Number:D6}";
        var lines = new List<PdfLine>
        {
            new(hospitalName, 20, Bold: true, Mono: false),
            new(paid ? "RECEIPT" : invoice.Status == Data.InvoiceStatus.Void ? "INVOICE - VOID" : "INVOICE - UNPAID", 14, Bold: true, Mono: false),
            new(""),
            new($"Number : {number}"),
            new($"Date   : {invoice.CreatedAt:dd MMM yyyy, HH:mm} UTC"),
            new($"Patient: {patientName}"),
            new($"Status : {invoice.Status.ToUpperInvariant()}"),
        };
        if (paid) lines.Add(new($"Paid   : {invoice.PaidAt:dd MMM yyyy, HH:mm} UTC by {invoice.PaymentMethod}"));
        lines.Add(new(""));
        lines.Add(new($"{"Description",-40}{"Qty",4}{"Price",12}{"Total",14}", Bold: true));
        lines.Add(new(new string('-', 70)));
        foreach (var i in invoice.Items.OrderBy(i => i.Source).ThenBy(i => i.Description))
            lines.Add(new($"{Fit(i.Description, 40)}{i.Quantity,4}{Money(i.UnitPrice),12}{Money(i.Quantity * i.UnitPrice),14}"));
        lines.Add(new(new string('-', 70)));
        lines.Add(new($"{"TOTAL (Rs.)",-56}{Money(invoice.Total),14}", Size: 11, Bold: true));
        lines.Add(new(""));
        lines.Add(new(paid ? "Thank you. This receipt was generated electronically." : "This invoice is not yet paid.", 9, Mono: false));
        return SimplePdf.Create(lines);
    }
}
