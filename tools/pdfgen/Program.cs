using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var outDir = @"c:\repo\invoice-agent\src\invledger_app\wwwroot";

var bloomberg = new Invoice
{
    VendorName = "Bloomberg L.P.",
    VendorAddress = new[] { "731 Lexington Avenue", "New York, NY 10022", "United States", "Tax ID: 13-3537585" },
    VendorContact = "billing@bloomberg.net  |  +1 212-318-2000",
    BillToName = "Accounts Payable",
    BillToAddress = new[] { "Finance Department", "100 Corporate Drive", "Suite 400", "Chicago, IL 60601" },
    InvoiceNumber = "INV-2026-0004",
    InvoiceDate = "May 16, 2026",
    DueDate = "June 15, 2026",
    ServicePeriod = "May 1 – May 31, 2026",
    PoNumber = "PO-2026-0411",
    Terms = "Net 30",
    Currency = "USD",
    Lines = new()
    {
        new("BBG-TERM-STD",    "Bloomberg Terminal License – Standard Seat",        5,  5000.00m, "seat/month"),
        new("BBG-ANY-MOB",     "Bloomberg Anywhere Mobile Access",                  5,   300.00m, "user/month"),
        new("BBG-DL-REF-T2",   "Data License – Reference Data Feed (Tier 2)",       1,  4200.00m, "month"),
        new("BBG-BPIPE",       "B-PIPE Managed Market Data Feed",                   1,  3800.00m, "month"),
        new("BBG-PORT-ENT",    "PORT Enterprise – Portfolio & Risk Analytics",      1,  2500.00m, "month"),
        new("BBG-AIM",         "AIM – Asset & Investment Manager",                  1,  3200.00m, "month"),
        new("BBG-MARS",        "MARS Multi-Asset Risk System Module",               1,  2100.00m, "month"),
        new("BBG-BMC-TRN",     "Bloomberg Market Concepts (BMC) Certification",     3,   250.00m, "user"),
        new("BBG-SUPPORT",     "Premium Support & SLA (24x7)",                      1,  1200.00m, "month"),
    },
    TaxLabel = "Sales Tax (NY 8.875%)",
    TaxRate = 0.08875m,
    Notes = "Wire transfer: Citibank N.A. | ABA 021000089 | Account 30001234 | SWIFT CITIUS33\nReference invoice number on remittance. Late payments accrue 1.5% interest per month."
};

var morningstar = new Invoice
{
    VendorName = "Morningstar Australasia Pty Limited",
    VendorAddress = new[] { "Level 3, International Tower 1", "100 Barangaroo Avenue", "Sydney NSW 2000", "Australia", "ABN 95 090 665 544" },
    VendorContact = "remittances.au@morningstar.com  |  +61 2 9276 4444",
    BillToName = "Accounts Payable",
    BillToAddress = new[] { "Finance Department", "Level 12, 350 Collins Street", "Melbourne VIC 3000", "Australia" },
    ShipToName = "Accounts Payable",
    ShipToAddress = new[] { "Finance Department", "Level 12, 350 Collins Street", "Melbourne VIC 3000", "Australia" },
    InvoiceNumber = "INV-2026-0005",
    InvoiceDate = "May 17, 2026",
    DueDate = "June 16, 2026",
    ServicePeriod = "01-May-2026 to 31-May-2026",
    ContractNumber = "MS-AU-778421",
    ContractTerm = "01-Jul-2025 to 30-Jun-2026",
    CustomerNumber = "C-0044219",
    PoNumber = "PO-2026-0418",
    Terms = "Net 30",
    Currency = "AUD",
    Lines = new()
    {
        new("ALG-PMP-15",  "AdviserLogic – Practice Management Platform (15 advisers)", 15, 150.00m, "adviser/mo"),
        new("ALG-DOCV",    "AdviserLogic – Document Vault Add-on",                       1, 480.00m, "month"),
        new("ALG-SOA",     "AdviserLogic – Statement of Advice Generator",               1, 620.00m, "month"),
        new("LIC-COMP",    "Licensee Solutions – Compliance Monitoring",                 1,1850.00m, "month"),
        new("LIC-APL",     "Licensee Solutions – Approved Product List Management",      1, 950.00m, "month"),
        new("LIC-AUDIT",   "Licensee Solutions – Adviser Audit Service",                 5, 220.00m, "audit"),
        new("MS-RES-AUEQ", "Morningstar Research – Australian Equities Coverage",        1,1400.00m, "month"),
        new("MS-DATA-MF",  "Morningstar Data – Managed Funds Performance Feed (AU)",     1,1650.00m, "month"),
        new("MS-SUPPORT",  "Premium Support & SLA",                                      1, 380.00m, "month"),
    },
    TaxLabel = "GST 10%",
    TaxRate = 0.10m,
    Notes = "Bank Transfer\nBank: Bank of America\nBranch: BOA Sydney, AU\nLevel 38, Governor Phillip Tower, 1 Farrer Place, Sydney NSW 2000, Australia\nAccount Name: Morningstar Australasia Pty Limited\nAccount Number: 704331482\nBSB: 232001\nSWIFT Code: BOFAAUSX\n\nPlease reference invoice and customer number with payment. To ensure your payment reaches your account, please submit your remittance details by email to remittances.au@morningstar.com or by Fax: +61 2 9276 4545. Questions? Call +61 2 9276 4444."
};

var factset = new Invoice
{
    VendorName = "FactSet Research Systems Inc.",
    VendorAddress = new[] { "45 Glover Avenue, 7th Floor", "Norwalk, CT 06850", "United States", "Tax ID: 13-3261323" },
    VendorContact = "ar@factset.com  |  +1 203-810-1000",
    BillToName = "Accounts Payable",
    BillToAddress = new[] { "Finance Department", "100 Corporate Drive", "Suite 400", "Chicago, IL 60601" },
    InvoiceNumber = "INV-2026-0006",
    InvoiceDate = "May 19, 2026",
    DueDate = "June 18, 2026",
    ServicePeriod = "May 1 – May 31, 2026",
    PoNumber = "PO-2026-0422",
    Terms = "Net 30",
    Currency = "USD",
    Lines = new()
    {
        new("FDS-WS-SEAT",     "FactSet Workstation License",                         8, 3000.00m, "seat/month"),
        new("FDS-FF-FEED",     "Data Feed – Fundamentals (FF)",                       1, 4500.00m, "month"),
        new("FDS-PA",          "Portfolio Analytics Module",                          1, 3200.00m, "month"),
        new("FDS-QRES",        "Quantitative Research – Alpha Testing",               1, 2800.00m, "month"),
        new("FDS-MA-DB",       "Mergers & Acquisitions Database",                     1, 1600.00m, "month"),
        new("FDS-OWN",         "Ownership Data Module",                               1, 1400.00m, "month"),
        new("FDS-EST-PREM",    "Estimates – Premium Data",                            1, 2100.00m, "month"),
        new("FDS-RISK",        "Risk Models Add-on",                                  1, 1750.00m, "month"),
        new("FDS-SUPPORT",     "Technical Support – Premium SLA",                     1,  900.00m, "month"),
    },
    TaxLabel = "Sales Tax (CT 6.35%)",
    TaxRate = 0.0635m,
    Notes = "Wire to: Bank of America N.A. | ABA 026009593 | Account 4427889911 | SWIFT BOFAUS3N\nReference invoice number on remittance. For inquiries: ar@factset.com."
};

InvoicePdf.Render(bloomberg).GeneratePdf(Path.Combine(outDir, "scenarios", "scenario-bloomberg", "bloomberg-may.pdf"));
MorningstarPdf.Render(morningstar).GeneratePdf(Path.Combine(outDir, "scenarios", "scenario-morningstar", "morningstar-may.pdf"));
InvoicePdf.Render(factset).GeneratePdf(Path.Combine(outDir, "scenarios", "scenario-factset", "factset-may.pdf"));

File.Copy(Path.Combine(outDir, "scenarios", "scenario-bloomberg",   "bloomberg-may.pdf"),   Path.Combine(outDir, "invoices", "bloomberg-may.pdf"),   true);
File.Copy(Path.Combine(outDir, "scenarios", "scenario-morningstar", "morningstar-may.pdf"), Path.Combine(outDir, "invoices", "morningstar-may.pdf"), true);
File.Copy(Path.Combine(outDir, "scenarios", "scenario-factset",     "factset-may.pdf"),     Path.Combine(outDir, "invoices", "factset-may.pdf"),     true);

// Remove obsolete invoice files for replaced scenarios.
foreach (var old in new[] { "apex-technology-may.pdf", "meridian-supply-may.pdf", "summit-facilities-may.pdf" })
{
    var p = Path.Combine(outDir, "invoices", old);
    if (File.Exists(p)) File.Delete(p);
}

Console.WriteLine("Generated 3 invoice PDFs.");

record LineItem(string Sku, string Description, decimal Quantity, decimal UnitPrice, string Unit)
{
    public decimal Total => Math.Round(Quantity * UnitPrice, 2);
}

class Invoice
{
    public string VendorName { get; set; } = "";
    public string[] VendorAddress { get; set; } = Array.Empty<string>();
    public string VendorContact { get; set; } = "";
    public string BillToName { get; set; } = "";
    public string[] BillToAddress { get; set; } = Array.Empty<string>();
    public string ShipToName { get; set; } = "";
    public string[] ShipToAddress { get; set; } = Array.Empty<string>();
    public string InvoiceNumber { get; set; } = "";
    public string InvoiceDate { get; set; } = "";
    public string DueDate { get; set; } = "";
    public string ServicePeriod { get; set; } = "";
    public string ContractNumber { get; set; } = "";
    public string ContractTerm { get; set; } = "";
    public string CustomerNumber { get; set; } = "";
    public string PoNumber { get; set; } = "";
    public string Terms { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public List<LineItem> Lines { get; set; } = new();
    public string TaxLabel { get; set; } = "Tax";
    public decimal TaxRate { get; set; }
    public string Notes { get; set; } = "";

    public decimal Subtotal => Math.Round(Lines.Sum(l => l.Total), 2);
    public decimal TaxAmount => Math.Round(Subtotal * TaxRate, 2);
    public decimal GrandTotal => Subtotal + TaxAmount;
}

static class InvoicePdf
{
    public static Document Render(Invoice inv) => Document.Create(c =>
    {
        c.Page(p =>
        {
            p.Size(PageSizes.Letter);
            p.Margin(36);
            p.DefaultTextStyle(t => t.FontSize(9).FontFamily("Helvetica"));

            p.Header().Element(h => Header(h, inv));
            p.Content().PaddingVertical(10).Element(co => Content(co, inv));
            p.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Medium));
                t.Span($"{inv.VendorName}  •  Invoice {inv.InvoiceNumber}  •  Page ");
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });
    });

    static void Header(QuestPDF.Infrastructure.IContainer h, Invoice inv)
    {
        h.Row(r =>
        {
            r.RelativeItem().Column(col =>
            {
                col.Item().Text(inv.VendorName).FontSize(16).Bold().FontColor(Colors.Blue.Darken3);
                foreach (var line in inv.VendorAddress)
                    col.Item().Text(line).FontSize(8).FontColor(Colors.Grey.Darken2);
                col.Item().PaddingTop(2).Text(inv.VendorContact).FontSize(8).FontColor(Colors.Grey.Darken2);
            });
            r.ConstantItem(180).AlignRight().Column(col =>
            {
                col.Item().AlignRight().Text("INVOICE").FontSize(22).Bold().FontColor(Colors.Grey.Darken3);
                col.Item().AlignRight().Text(inv.InvoiceNumber).FontSize(10).SemiBold();
            });
        });
    }

    static void Content(QuestPDF.Infrastructure.IContainer co, Invoice inv)
    {
        co.Column(col =>
        {
            // Meta
            col.Item().PaddingTop(4).Row(r =>
            {
                r.RelativeItem().Background(Colors.Grey.Lighten4).Padding(8).Column(c =>
                {
                    c.Item().Text("Bill To").Bold().FontSize(9).FontColor(Colors.Grey.Darken3);
                    c.Item().Text(inv.BillToName).SemiBold();
                    foreach (var line in inv.BillToAddress)
                        c.Item().Text(line).FontSize(8);
                });
                r.ConstantItem(10);
                r.RelativeItem().Background(Colors.Grey.Lighten4).Padding(8).Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(3); });
                    void Row(string k, string v)
                    {
                        t.Cell().Text(k).SemiBold().FontSize(8).FontColor(Colors.Grey.Darken3);
                        t.Cell().Text(v).FontSize(8);
                    }
                    Row("Invoice Date", inv.InvoiceDate);
                    Row("Due Date", inv.DueDate);
                    Row("Service Period", inv.ServicePeriod);
                    Row("PO Number", inv.PoNumber);
                    Row("Payment Terms", inv.Terms);
                    Row("Currency", inv.Currency);
                });
            });

            // Line items
            col.Item().PaddingTop(14).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(80);
                    cd.RelativeColumn(5);
                    cd.ConstantColumn(45);
                    cd.ConstantColumn(70);
                    cd.ConstantColumn(70);
                    cd.ConstantColumn(75);
                });
                t.Header(h =>
                {
                    void Th(string text, bool right = false)
                    {
                        var cell = h.Cell().Background(Colors.Blue.Darken3).Padding(5);
                        var span = cell.Text(text).FontColor(Colors.White).SemiBold().FontSize(9);
                        if (right) cell.AlignRight();
                    }
                    Th("SKU");
                    Th("Description");
                    Th("Qty");
                    Th("Unit");
                    h.Cell().Background(Colors.Blue.Darken3).Padding(5).AlignRight().Text("Unit Price").FontColor(Colors.White).SemiBold().FontSize(9);
                    h.Cell().Background(Colors.Blue.Darken3).Padding(5).AlignRight().Text("Amount").FontColor(Colors.White).SemiBold().FontSize(9);
                });

                int i = 0;
                foreach (var line in inv.Lines)
                {
                    var bg = (i++ % 2 == 0) ? Colors.White : Colors.Grey.Lighten5;
                    t.Cell().Background(bg).Padding(5).Text(line.Sku).FontSize(8);
                    t.Cell().Background(bg).Padding(5).Text(line.Description).FontSize(8);
                    t.Cell().Background(bg).Padding(5).Text(line.Quantity.ToString("0.##")).FontSize(8);
                    t.Cell().Background(bg).Padding(5).Text(line.Unit).FontSize(8);
                    t.Cell().Background(bg).Padding(5).AlignRight().Text(line.UnitPrice.ToString("N2")).FontSize(8);
                    t.Cell().Background(bg).Padding(5).AlignRight().Text(line.Total.ToString("N2")).FontSize(8);
                }
            });

            // Totals
            col.Item().PaddingTop(10).AlignRight().Width(260).Table(t =>
            {
                t.ColumnsDefinition(cd => { cd.RelativeColumn(3); cd.RelativeColumn(2); });
                void Row(string label, decimal amount, bool bold = false)
                {
                    var l = t.Cell().PaddingVertical(3).PaddingHorizontal(6).Text(label).FontSize(9);
                    if (bold) l.SemiBold();
                    var r = t.Cell().PaddingVertical(3).PaddingHorizontal(6).AlignRight().Text($"{inv.Currency} {amount:N2}").FontSize(9);
                    if (bold) r.SemiBold();
                }
                Row("Subtotal", inv.Subtotal);
                Row(inv.TaxLabel, inv.TaxAmount);
                t.Cell().ColumnSpan(2).PaddingTop(2).BorderTop(1).BorderColor(Colors.Grey.Darken1);
                t.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Total Due").FontColor(Colors.White).Bold();
                t.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight().Text($"{inv.Currency} {inv.GrandTotal:N2}").FontColor(Colors.White).Bold();
            });

            // Notes
            col.Item().PaddingTop(18).Text("Payment Instructions").Bold().FontSize(9).FontColor(Colors.Grey.Darken3);
            col.Item().PaddingTop(2).Text(inv.Notes).FontSize(8).FontColor(Colors.Grey.Darken2);
        });
    }
}

static class MorningstarPdf
{
    // Salmon/pink highlights used in the reference invoice layout.
    const string Pink = "#F4C8C0";
    const string PinkLight = "#F9DDD7";
    const string Red = "#E5202E";
    const string TextDark = "#1F1F1F";
    const string TextMuted = "#5C5C5C";

    public static Document Render(Invoice inv) => Document.Create(c =>
    {
        c.Page(p =>
        {
            p.Size(PageSizes.A4);
            p.Margin(32);
            p.DefaultTextStyle(t => t.FontSize(9).FontFamily("Helvetica").FontColor(TextDark));

            p.Header().Element(h => Header(h, inv));
            p.Content().PaddingVertical(10).Element(co => Content(co, inv));
        });
    });

    static void Header(QuestPDF.Infrastructure.IContainer h, Invoice inv)
    {
        h.Row(r =>
        {
            r.RelativeItem().Column(col =>
            {
                col.Item().Text("MORNINGSTAR").FontSize(20).Bold().FontColor(Red).LetterSpacing(0.05f);
                col.Item().PaddingTop(1).Text(inv.VendorName).FontSize(8).FontColor(TextMuted);
            });
            r.RelativeItem().AlignCenter().PaddingTop(4).Text("Tax Invoice").FontSize(14).Bold();
            r.RelativeItem().AlignRight().PaddingTop(6).Text("Page 1 of 1").FontSize(8).FontColor(TextMuted);
        });
    }

    static void LabelBox(QuestPDF.Infrastructure.IContainer cell, string label, string value)
    {
        cell.Background(Pink).Padding(6).Column(c =>
        {
            c.Item().Text(label).Bold().FontSize(8);
            c.Item().PaddingTop(2).Text(value).FontSize(9);
        });
    }

    static void Content(QuestPDF.Infrastructure.IContainer co, Invoice inv)
    {
        co.Column(col =>
        {
            // Top meta row: Customer Number | Contract Number / Term | Invoice Number / Date
            col.Item().PaddingTop(8).Row(r =>
            {
                r.RelativeItem().Element(c => LabelBox(c, "Customer Number", inv.CustomerNumber));
                r.ConstantItem(8);
                r.RelativeItem().Column(c =>
                {
                    c.Item().Element(x => LabelBox(x, "Contract Number", inv.ContractNumber));
                    c.Item().PaddingTop(4).Element(x => LabelBox(x, "Contract Term", inv.ContractTerm));
                });
                r.ConstantItem(8);
                r.RelativeItem().Column(c =>
                {
                    c.Item().Element(x => LabelBox(x, "Invoice Number", inv.InvoiceNumber));
                    c.Item().PaddingTop(4).Element(x => LabelBox(x, "Invoice Date", inv.InvoiceDate));
                });
            });

            col.Item().PaddingTop(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

            // Bill to / Ship to
            col.Item().PaddingTop(10).Row(r =>
            {
                r.RelativeItem().Background(Pink).Padding(6).Column(c =>
                {
                    c.Item().Text("Bill to").Bold().FontSize(9);
                    c.Item().PaddingTop(2).Text(inv.BillToName).FontSize(8);
                    foreach (var l in inv.BillToAddress) c.Item().Text(l).FontSize(8);
                });
                r.ConstantItem(12);
                r.RelativeItem().Background(Pink).Padding(6).Column(c =>
                {
                    c.Item().Text("Ship to").Bold().FontSize(9);
                    c.Item().PaddingTop(2).Text(inv.ShipToName).FontSize(8);
                    foreach (var l in inv.ShipToAddress) c.Item().Text(l).FontSize(8);
                });
            });

            // Line items: Description | Morningstar ID | Amount | Tax | Total
            col.Item().PaddingTop(18).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(5);
                    cd.ConstantColumn(90);
                    cd.ConstantColumn(70);
                    cd.ConstantColumn(50);
                    cd.ConstantColumn(75);
                });
                t.Header(h =>
                {
                    h.Cell().Background(Pink).Padding(6).Text("Description").Bold().FontSize(9);
                    h.Cell().Background(PinkLight).Padding(6).Text("Morningstar ID").Bold().FontSize(9);
                    h.Cell().Background(PinkLight).Padding(6).AlignRight().Text("Amount").Bold().FontSize(9);
                    h.Cell().Background(PinkLight).Padding(6).AlignRight().Text("Tax").Bold().FontSize(9);
                    h.Cell().Background(PinkLight).Padding(6).AlignRight().Text("Total").Bold().FontSize(9);
                });

                foreach (var line in inv.Lines)
                {
                    var taxAmt = Math.Round(line.Total * inv.TaxRate, 2);
                    var total = line.Total + taxAmt;
                    t.Cell().Padding(6).Column(c =>
                    {
                        c.Item().Text(line.Description).FontSize(9);
                        c.Item().Text(inv.ServicePeriod).FontSize(8).FontColor(TextMuted);
                    });
                    t.Cell().Padding(6).Text(line.Sku).FontSize(9);
                    t.Cell().Padding(6).AlignRight().Text(line.Total.ToString("N2")).FontSize(9);
                    t.Cell().Padding(6).AlignRight().Text($"{inv.TaxRate * 100:0}%").FontSize(9);
                    t.Cell().Padding(6).AlignRight().Text(total.ToString("N2")).FontSize(9);
                }
            });

            col.Item().PaddingTop(14).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

            // Totals block
            col.Item().PaddingTop(10).Background(PinkLight).Padding(8).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(4);
                    cd.ConstantColumn(60);
                    cd.ConstantColumn(80);
                    cd.ConstantColumn(80);
                    cd.ConstantColumn(90);
                });
                void Span(string label, string? a = null, string? b = null, string? c = null, string? d = null, bool bold = false)
                {
                    var l = t.Cell().PaddingVertical(2).Text(label).FontSize(9);
                    if (bold) l.Bold();
                    void Val(string? v)
                    {
                        var cell = t.Cell().PaddingVertical(2).AlignRight().Text(v ?? "").FontSize(9);
                        if (bold) cell.Bold();
                    }
                    Val(a); Val(b); Val(c); Val(d);
                }
                t.Cell().ColumnSpan(5).Text("Contracted Currency").Bold().FontSize(9);
                Span($"Invoice Total ({inv.TaxLabel})", null, inv.Subtotal.ToString("N2"), inv.TaxAmount.ToString("N2"), inv.GrandTotal.ToString("N2"));
                Span("Payments", null, null, null, "0.00");
                Span("Credits/Adjustments", null, null, null, "0.00");
                Span("Balance Due", inv.Currency, null, null, inv.GrandTotal.ToString("N2"), bold: true);
                Span("Terms of Payment", null, null, null, inv.Terms.ToUpper());
                Span("Due Date", null, null, null, inv.DueDate, bold: true);
            });

            // Bank transfer / notes
            col.Item().PaddingTop(12).Text("Bank Transfer").Bold().FontSize(9);
            col.Item().PaddingTop(2).Text(inv.Notes).FontSize(8).FontColor(TextMuted);
        });
    }
}
