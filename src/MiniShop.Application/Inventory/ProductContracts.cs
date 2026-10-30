using System.ComponentModel.DataAnnotations;

namespace MiniShop.Application.Inventory;

public sealed record ProductDto(
    int Id,
    string Name,
    string? Description,
    decimal Price,
    int StockQuantity,
    int CategoryId,
    string CategoryName);

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

    /// <summary>E-shop krok 2: každý produkt patří do kategorie.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Vyberte kategorii.")]
    [Display(Name = "Kategorie")]
    public int CategoryId { get; set; }
}

public sealed class UpdateProductRequest
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

    [Range(1, int.MaxValue, ErrorMessage = "Vyberte kategorii.")]
    [Display(Name = "Kategorie")]
    public int CategoryId { get; set; }
}

public sealed class AddStockRequest
{
    [Range(1, 100_000)]
    public int Quantity { get; set; }
}

/// <summary>E-shop krok 2: kategorie pro administraci, s počtem produktů (smazat jde jen prázdnou).</summary>
public sealed record CategoryDto(int Id, string Name, int ProductCount);

public sealed class CategoryRequest
{
    [Required(ErrorMessage = "Název kategorie je povinný.")]
    [StringLength(50)]
    [Display(Name = "Název kategorie")]
    public string Name { get; set; } = string.Empty;
}
