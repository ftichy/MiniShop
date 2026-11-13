using MiniShop.Application.Abstractions;

namespace MiniShop.Tests.Application;

/// <summary>Převod UTC → český čas nezávisí na zóně stroje (CI běží v UTC, vývojář na Windows v české zóně).</summary>
public sealed class CzechTimeTests
{
    private static DateTime Utc(int y, int m, int d, int h, int min) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    [Fact]
    public void Zima_JeUtcPlusJedna() =>
        Assert.Equal(new DateTime(2026, 1, 15, 11, 0, 0), CzechTime.FromUtc(Utc(2026, 1, 15, 10, 0)));

    [Fact]
    public void Leto_JeUtcPlusDve() =>
        Assert.Equal(new DateTime(2026, 7, 15, 12, 0, 0), CzechTime.FromUtc(Utc(2026, 7, 15, 10, 0)));

    [Fact]
    public void PrechodNaLetniCas_29Brezna2026()
    {
        Assert.Equal(new DateTime(2026, 3, 29, 1, 59, 0), CzechTime.FromUtc(Utc(2026, 3, 29, 0, 59)));
        Assert.Equal(new DateTime(2026, 3, 29, 3, 0, 0), CzechTime.FromUtc(Utc(2026, 3, 29, 1, 0)));
    }

    [Fact]
    public void PrechodNaZimniCas_25Rijna2026()
    {
        Assert.Equal(new DateTime(2026, 10, 25, 2, 59, 0), CzechTime.FromUtc(Utc(2026, 10, 25, 0, 59)));
        Assert.Equal(new DateTime(2026, 10, 25, 2, 0, 0), CzechTime.FromUtc(Utc(2026, 10, 25, 1, 0)));
    }

    [Fact]
    public void Unspecified_ZDatabazeSeBereJakoUtc() =>
        Assert.Equal(new DateTime(2026, 7, 15, 12, 0, 0),
            CzechTime.FromUtc(new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Unspecified)));

    [Fact]
    public void ToUtc_JeOpakemFromUtc() =>
        Assert.Equal(Utc(2026, 7, 14, 22, 0), CzechTime.ToUtc(new DateTime(2026, 7, 15, 0, 0, 0)));

    [Fact]
    public void Format_PouzivaCeskyFormat()
    {
        Assert.Equal("13. 11. 2026 10:14", CzechTime.Format(Utc(2026, 11, 13, 9, 14)));
        Assert.Equal("13. 7. 2026", Utc(2026, 7, 12, 23, 30).ToCzechString("d. M. yyyy"));
    }
}
