using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Reviews;

namespace MiniShop.Tests.Domain;

/// <summary>E-shop krok 14: pravidla hodnocení a kopie nákupů (kdo smí hodnotit) – bez databáze a zpráv.</summary>
public class ReviewTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Hodnoceni_MimoRozsahHvezd_Chyba(int rating)
    {
        Assert.Throws<DomainException>(() => Review.Create(1, "zakaznik-1", "Jan Novák", rating, null, Now));
    }

    [Fact]
    public void Hodnoceni_TextOrezanyAOmezeny()
    {
        var review = Review.Create(1, "zakaznik-1", "Jan Novák", 5, "  Skvělé  ", Now);
        Assert.Equal("Skvělé", review.Text);

        Assert.Throws<DomainException>(() => review.Edit(4, new string('x', Review.TextMaxLength + 1), Now));
    }

    [Theory]
    [InlineData("Jan Novák", "Jan N.")]
    [InlineData("Jan Karel novák", "Jan N.")]
    [InlineData("Jan", "Jan")]
    [InlineData("  ", "Zákazník")]
    public void Autor_JenKrestniJmenoAIniciala(string fullName, string expected)
    {
        Assert.Equal(expected, Review.ShortName(fullName));
    }

    [Fact]
    public void Autor_PoSmazaniUctuAnonymni()
    {
        var review = Review.Create(1, "zakaznik-1", "Jan Novák", 5, null, Now);
        review.AnonymizeAuthor();

        Assert.Equal((string.Empty, Review.AnonymizedAuthor), (review.CustomerId, review.AuthorName));
    }

    [Fact]
    public void Nakup_HodnotitJenDoruceneANevracene()
    {
        var order = PurchasedOrder.Create(42);
        order.ApplyPlaced("zakaznik-1", "Jan Novák", [7, 8]);
        Assert.False(order.AllowsReviewOf("zakaznik-1", 7));   // ještě nedoručeno

        order.MarkDelivered();
        Assert.True(order.AllowsReviewOf("zakaznik-1", 7));
        Assert.False(order.AllowsReviewOf("zakaznik-2", 7));   // cizí objednávka
        Assert.False(order.AllowsReviewOf("zakaznik-1", 9));   // produkt v objednávce není

        order.MarkReturned();
        Assert.False(order.AllowsReviewOf("zakaznik-1", 7));
    }

    [Fact]
    public void Nakup_DoruceniPredbehloVznik_VysledekStejny()
    {
        var order = PurchasedOrder.Create(42);
        order.MarkDelivered();                                 // OrderCompletedV1 dorazila dřív
        order.ApplyPlaced("zakaznik-1", "Jan Novák", [7, 7]);  // OrderPlacedV2 až potom (produkt dvakrát = jednou)

        Assert.True(order.AllowsReviewOf("zakaznik-1", 7));
        Assert.Single(order.Products);
    }
}
