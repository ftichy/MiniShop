using MiniShop.Web.Pages.Admin.Coupons;

namespace MiniShop.Tests.Web;

/// <summary>
/// Kupon „platí do“ = konec dne v českém čase, ne v zóně serveru (v kontejneru UTC – kupon by platil o hodinu, v létě o dvě déle).
/// </summary>
public sealed class CouponValidityTests
{
    [Fact]
    public void PlatiDo_KonecDneVCeskemCase()
    {
        // Zima (SEČ, UTC+1): konec 31. 12. v Praze = 22:59:59 UTC.
        Assert.Equal(new DateTime(2026, 12, 31, 22, 59, 59, DateTimeKind.Utc), Truncate(IndexModel.EndOfDayUtc(new DateOnly(2026, 12, 31))));
        // Léto (SELČ, UTC+2): konec 31. 7. v Praze = 21:59:59 UTC.
        Assert.Equal(new DateTime(2026, 7, 31, 21, 59, 59, DateTimeKind.Utc), Truncate(IndexModel.EndOfDayUtc(new DateOnly(2026, 7, 31))));
        Assert.Equal(DateTimeKind.Utc, IndexModel.EndOfDayUtc(new DateOnly(2026, 1, 1)).Kind);
    }

    private static DateTime Truncate(DateTime value) => value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));
}
