using System.ComponentModel.DataAnnotations;

namespace MiniShop.Application.Inventory;

public sealed record ProductDto(int Id, string Name, string? Description, decimal Price, int StockQuantity);

public sealed class CreateProductRequest
{
    [Required(ErrorMessage = "Název je povinný.")]
    [StringLength(100)]
    [Display(Name = "Název")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Popis")]
    public string? Description { get; set; }

    [Range(0.01, 1_000_000, ErrorMessage = "Cena musí být mezi 0,01 a 1 000 000.")]
    [Display(Name = "Cena (Kč)")]
    public decimal Price { get; set; }

    [Range(0, 100_000, ErrorMessage = "Zásoba musí být mezi 0 a 100 000.")]
    [Display(Name = "Skladem (ks)")]
    public int StockQuantity { get; set; }
}

public sealed class UpdateProductRequest
{
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Range(0.01, 1_000_000)]
    public decimal Price { get; set; }
}

public sealed class AddStockRequest
{
    [Range(1, 100_000)]
    public int Quantity { get; set; }
}
