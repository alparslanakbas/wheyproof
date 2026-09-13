using IndirimTakip.Core.Entities;
using IndirimTakip.Infrastructure.Coupons;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IndirimTakip.Infrastructure.Tests;

public class CouponModelTests
{
    [Theory]
    [InlineData("SSN", null, true)]
    [InlineData(null, "bodybuilding.com", true)]
    [InlineData("SSN", "bodybuilding.com", false)]
    [InlineData(null, null, false)]
    [InlineData(" ", " ", false)]
    public void Coupon_request_needs_exactly_one_target(string? brandName, string? seller, bool expected)
    {
        var request = new CreateCouponRequest(brandName, seller, "CODE", "Description", null);

        Assert.Equal(expected, request.HasExactlyOneTarget);
    }

    [Fact]
    public void Coupon_belongs_to_either_a_brand_or_a_seller()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_test;Username=model_test;Password=model_test")
            .Options;

        using var db = new AppDbContext(options);
        var designTimeModel = db.GetService<IDesignTimeModel>().Model;
        var coupon = designTimeModel.FindEntityType(typeof(Coupon));

        Assert.NotNull(coupon);
        Assert.True(coupon.FindProperty(nameof(Coupon.BrandId))!.IsNullable);
        Assert.Equal(200, coupon.FindProperty(nameof(Coupon.Seller))!.GetMaxLength());

        // The code is OPTIONAL: not every promotion has one. An automatic first-order
        // discount for new members applies by itself; if the code were required, the
        // promotion either couldn't be shown at all or an empty code badge would send
        // shoppers looking for a code that doesn't exist.
        Assert.True(coupon.FindProperty(nameof(Coupon.Code))!.IsNullable);

        var constraint = Assert.Single(
            coupon.GetCheckConstraints(),
            c => c.Name == "CK_Coupons_ExactlyOneTarget");
        Assert.Equal("(\"BrandId\" IS NULL) <> (\"Seller\" IS NULL)", constraint.Sql);
    }
}
