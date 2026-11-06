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
        Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(style => style.FontSize(10));

            page.Header().Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text("Faktura").FontSize(22).Bold();
                    column.Item().Text($"číslo {invoice.Number}").FontSize(12);
                });
                row.RelativeItem().AlignRight().Column(column =>
                {
                    column.Item().Text($"Datum vystavení: {Date(invoice.IssuedAtUtc)}");
                    column.Item().Text($"Datum úhrady: {Date(invoice.PaidAtUtc)}");
                    column.Item().Text($"Objednávka: {invoice.OrderId}");
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
                        party.Item().Text(invoice.CustomerName);
                        if (!string.IsNullOrWhiteSpace(invoice.DeliveryAddress))
                        {
                            party.Item().Text(invoice.DeliveryAddress);
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

                    foreach (var line in invoice.Lines)
                    {
                        BodyCell(table.Cell(), line.ProductName);
                        BodyCell(table.Cell().AlignRight(), line.Quantity.ToString(Czech));
                        BodyCell(table.Cell().AlignRight(), Money(line.UnitPrice));
                        BodyCell(table.Cell().AlignRight(), line.Discount > 0 ? "−" + Money(line.Discount) : "");
                        BodyCell(table.Cell().AlignRight(), Money(line.LineTotal));
                    }

                    if (invoice.ShippingPrice > 0 || !string.IsNullOrEmpty(ShippingLabel(invoice.ShippingMethod)))
                    {
                        BodyCell(table.Cell(), ShippingLabel(invoice.ShippingMethod) ?? "Doprava");
                        BodyCell(table.Cell().AlignRight(), "1");
                        BodyCell(table.Cell().AlignRight(), Money(invoice.ShippingPrice));
                        BodyCell(table.Cell().AlignRight(), "");
                        BodyCell(table.Cell().AlignRight(), Money(invoice.ShippingPrice));
                    }
                });

                column.Item().AlignRight().Text($"Celkem: {Money(invoice.Total)}").FontSize(14).Bold();
                column.Item().Text($"{invoice.PaymentNote} – neplaťte znovu.");
                column.Item().Text(seller.Note).FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span($"Faktura {invoice.Number} · strana ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        })).GeneratePdf();

    private static void HeaderCell(IContainer cell, string text) =>
        cell.BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingVertical(4).Text(text).Bold();

    private static void BodyCell(IContainer cell, string text) =>
        cell.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Text(text);

    private static string Money(decimal amount) => amount.ToString("N2", Czech) + " Kč";

    private static string Date(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Prague).ToString("d. M. yyyy", Czech);

    private static string? ShippingLabel(string shippingMethod) => shippingMethod switch
    {
        "Courier" => "Doprava kurýrem",
        "Pickup" => "Osobní odběr",
        _ => null
    };
}
