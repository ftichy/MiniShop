using System.Globalization;
using MiniShop.Application.Invoicing;
using MiniShop.Domain.Invoicing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MiniShop.Infrastructure.Invoicing;

/// <summary>
/// E-shop krok 15: PDF faktury přes QuestPDF (layout popsaný v C#, žádné HTML ani prohlížeč). Písmo Lato je součástí
/// knihovny – české znaky fungují i v kontejneru bez systémových fontů. Licence Community (výukový projekt).
/// E-shop krok 15B: dobropis má stejný layout – liší se nadpisem, odkazem na fakturu a částkami se znaménkem minus.
/// </summary>
public sealed class QuestPdfInvoiceRenderer : IInvoicePdfRenderer
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    static QuestPdfInvoiceRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(Invoice invoice, InvoiceSeller seller) =>
        Render(new DocumentModel(
            Title: "Faktura",
            Number: invoice.Number,
            Facts:
            [
                $"Datum vystavení: {Date(invoice.IssuedAtUtc)}",
                $"Datum úhrady: {Date(invoice.PaidAtUtc)}",
                $"Objednávka: {invoice.OrderId}"
            ],
            CustomerName: invoice.CustomerName,
            DeliveryAddress: invoice.DeliveryAddress,
            Lines: invoice.Lines,
            ShippingMethod: invoice.ShippingMethod,
            ShippingPrice: invoice.ShippingPrice,
            Total: invoice.Total,
            Sign: 1,
            Notes: [$"{invoice.PaymentNote} – neplaťte znovu."]),
            seller);

    public byte[] Render(CreditNote creditNote, InvoiceSeller seller) =>
        Render(new DocumentModel(
            Title: "Dobropis",
            Number: creditNote.Number,
            Facts:
            [
                $"Datum vystavení: {Date(creditNote.IssuedAtUtc)}",
                $"K faktuře: {creditNote.InvoiceNumber}",
                $"Objednávka: {creditNote.OrderId}"
            ],
            CustomerName: creditNote.CustomerName,
            DeliveryAddress: creditNote.DeliveryAddress,
            Lines: creditNote.Lines,
            ShippingMethod: creditNote.ShippingMethod,
            ShippingPrice: creditNote.ShippingPrice,
            Total: creditNote.Total,
            Sign: -1,
            Notes:
            [
                $"Opravný doklad k faktuře č. {creditNote.InvoiceNumber}. Důvod: {ReasonLabel(creditNote.Reason)}.",
                $"{creditNote.RefundNote}: {Money(creditNote.Total)}."
            ]),
            seller);

    private static byte[] Render(DocumentModel model, InvoiceSeller seller) =>
        Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(style => style.FontSize(10));

            page.Header().Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text(model.Title).FontSize(22).Bold();
                    column.Item().Text($"číslo {model.Number}").FontSize(12);
                });
                row.RelativeItem().AlignRight().Column(column =>
                {
                    foreach (var fact in model.Facts)
                    {
                        column.Item().Text(fact);
                    }
                });
            });

            page.Content().PaddingVertical(20).Column(column =>
            {
                column.Spacing(16);

                column.Item().Row(row =>
                {
                    row.Spacing(20);
                    row.RelativeItem().Column(party =>
                    {
                        party.Item().Text("Dodavatel").Bold();
                        party.Item().Text(seller.Name);
                        party.Item().Text(seller.Address);
                        if (!string.IsNullOrWhiteSpace(seller.CompanyId))
                        {
                            party.Item().Text($"IČO: {seller.CompanyId}");
                        }
                    });
                    row.RelativeItem().Column(party =>
                    {
                        party.Item().Text("Odběratel").Bold();
                        party.Item().Text(model.CustomerName);
                        if (!string.IsNullOrWhiteSpace(model.DeliveryAddress))
                        {
                            party.Item().Text(model.DeliveryAddress);
                        }
                    });
                });

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(5);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                    });

                    table.Header(header =>
                    {
                        HeaderCell(header.Cell(), "Položka");
                        HeaderCell(header.Cell().AlignRight(), "Ks");
                        HeaderCell(header.Cell().AlignRight(), "Cena/ks");
                        HeaderCell(header.Cell().AlignRight(), "Sleva");
                        HeaderCell(header.Cell().AlignRight(), "Celkem");
                    });

                    foreach (var line in model.Lines)
                    {
                        BodyCell(table.Cell(), line.ProductName);
                        BodyCell(table.Cell().AlignRight(), (model.Sign * line.Quantity).ToString(Czech));
                        BodyCell(table.Cell().AlignRight(), Money(line.UnitPrice));
                        BodyCell(table.Cell().AlignRight(), line.Discount > 0 ? "−" + Money(line.Discount) : "");
                        BodyCell(table.Cell().AlignRight(), Money(model.Sign * line.LineTotal));
                    }

                    if (model.ShippingPrice > 0 || !string.IsNullOrEmpty(ShippingLabel(model.ShippingMethod)))
                    {
                        BodyCell(table.Cell(), ShippingLabel(model.ShippingMethod) ?? "Doprava");
                        BodyCell(table.Cell().AlignRight(), model.Sign.ToString(Czech));
                        BodyCell(table.Cell().AlignRight(), Money(model.ShippingPrice));
                        BodyCell(table.Cell().AlignRight(), "");
                        BodyCell(table.Cell().AlignRight(), Money(model.Sign * model.ShippingPrice));
                    }
                });

                column.Item().AlignRight().Text($"Celkem: {Money(model.Sign * model.Total)}").FontSize(14).Bold();
                foreach (var note in model.Notes)
                {
                    column.Item().Text(note);
                }

                column.Item().Text(seller.Note).FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span($"{model.Title} {model.Number} · strana ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        })).GeneratePdf();

    private static void HeaderCell(IContainer cell, string text) =>
        cell.BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingVertical(4).Text(text).Bold();

    private static void BodyCell(IContainer cell, string text) =>
        cell.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Text(text);

    // Záporná částka s typografickým minus (−), ne spojovníkem.
    private static string Money(decimal amount) =>
        (amount < 0 ? "−" : "") + Math.Abs(amount).ToString("N2", Czech) + " Kč";

    private static string Date(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Prague).ToString("d. M. yyyy", Czech);

    private static string? ShippingLabel(string shippingMethod) => shippingMethod switch
    {
        "Courier" => "Doprava kurýrem",
        "Pickup" => "Osobní odběr",
        _ => null
    };

    private static string ReasonLabel(CreditNoteReason reason) => reason switch
    {
        CreditNoteReason.Cancelled => "zrušená objednávka",
        CreditNoteReason.Rejected => "zamítnutá objednávka",
        CreditNoteReason.Returned => "vrácené zboží",
        _ => reason.ToString()
    };

    /// <summary>Co se na doklad tiskne – faktura i dobropis mají stejný layout. <c>Sign</c> −1 = dobropis.</summary>
    private sealed record DocumentModel(
        string Title,
        string Number,
        IReadOnlyList<string> Facts,
        string CustomerName,
        string? DeliveryAddress,
        IReadOnlyCollection<InvoiceLine> Lines,
        string ShippingMethod,
        decimal ShippingPrice,
        decimal Total,
        int Sign,
        IReadOnlyList<string> Notes);
}
