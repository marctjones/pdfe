using FluentAssertions;
using Pdfe.Core.ColorSpaces;
using Xunit;

namespace Pdfe.Core.Tests.ColorSpaces;

public class PdfColorSpaceTests
{
    [Fact]
    public void DeviceRgb_ToRgb_IdentityMapping()
    {
        var cs = PdfColorSpace.DeviceRGB;
        var (r, g, b) = cs.ToRgb(new[] { 0.5, 0.5, 0.5 });
        r.Should().Be(0.5);
        g.Should().Be(0.5);
        b.Should().Be(0.5);
    }

    [Fact]
    public void DeviceCmyk_Black_ConvertsToBlack()
    {
        var cs = PdfColorSpace.DeviceCMYK;
        var (r, g, b) = cs.ToRgb(new[] { 0.0, 0.0, 0.0, 1.0 });
        r.Should().Be(0.0);
        g.Should().Be(0.0);
        b.Should().Be(0.0);
    }

    [Fact]
    public void DeviceCmyk_White_ConvertsToWhite()
    {
        var cs = PdfColorSpace.DeviceCMYK;
        var (r, g, b) = cs.ToRgb(new[] { 0.0, 0.0, 0.0, 0.0 });
        r.Should().Be(1.0);
        g.Should().Be(1.0);
        b.Should().Be(1.0);
    }

    [Fact]
    public void DeviceCmyk_Yellow_ConvertsCorrectly()
    {
        var cs = PdfColorSpace.DeviceCMYK;
        var (r, g, b) = cs.ToRgb(new[] { 0.0, 0.0, 1.0, 0.0 });
        r.Should().BeApproximately(1.0, 0.01);
        g.Should().BeApproximately(1.0, 0.01);
        b.Should().BeApproximately(0.0, 0.01);
    }

    [Fact]
    public void DeviceGray_Half_ConvertsToDarkGray()
    {
        var cs = PdfColorSpace.DeviceGray;
        var (r, g, b) = cs.ToRgb(new[] { 0.5 });
        r.Should().Be(0.5);
        g.Should().Be(0.5);
        b.Should().Be(0.5);
    }

    [Fact]
    public void FromName_DeviceRgb_ReturnsRgbColorSpace()
    {
        var cs = PdfColorSpace.FromName("DeviceRGB");
        cs.Type.Should().Be(PdfColorSpaceType.DeviceRGB);
        cs.Components.Should().Be(3);
    }

    [Fact]
    public void FromName_DeviceGray_ReturnsGrayColorSpace()
    {
        var cs = PdfColorSpace.FromName("DeviceGray");
        cs.Type.Should().Be(PdfColorSpaceType.DeviceGray);
        cs.Components.Should().Be(1);
    }

    [Fact]
    public void FromName_DeviceCmyk_ReturnsCmykColorSpace()
    {
        var cs = PdfColorSpace.FromName("DeviceCMYK");
        cs.Type.Should().Be(PdfColorSpaceType.DeviceCMYK);
        cs.Components.Should().Be(4);
    }

    [Fact]
    public void FromName_UnknownName_ReturnsUnknownColorSpace()
    {
        var cs = PdfColorSpace.FromName("UnknownColorSpace");
        cs.Type.Should().Be(PdfColorSpaceType.Unknown);
        cs.Components.Should().Be(1);
    }

    [Fact]
    public void DeviceGray_Zero_ConvertsToBlack()
    {
        var cs = PdfColorSpace.DeviceGray;
        var (r, g, b) = cs.ToRgb(new[] { 0.0 });
        r.Should().Be(0.0);
        g.Should().Be(0.0);
        b.Should().Be(0.0);
    }

    [Fact]
    public void DeviceGray_One_ConvertsToWhite()
    {
        var cs = PdfColorSpace.DeviceGray;
        var (r, g, b) = cs.ToRgb(new[] { 1.0 });
        r.Should().Be(1.0);
        g.Should().Be(1.0);
        b.Should().Be(1.0);
    }

    [Fact]
    public void ToRgb_EmptyArray_ReturnsBlack()
    {
        var cs = PdfColorSpace.DeviceRGB;
        var (r, g, b) = cs.ToRgb(Array.Empty<double>());
        r.Should().Be(0);
        g.Should().Be(0);
        b.Should().Be(0);
    }

    [Fact]
    public void ToRgb_InsufficientComponents_UsesAvailable()
    {
        var cs = PdfColorSpace.DeviceRGB;
        var (r, g, b) = cs.ToRgb(new[] { 0.8 });
        r.Should().Be(0.8);
        g.Should().Be(0.8);
        b.Should().Be(0.8);
    }

    [Fact]
    public void CalGray_ToRgb_ConvertsLikeDeviceGray()
    {
        var calGray = PdfColorSpace.FromName("CalGray");
        calGray.Type.Should().Be(PdfColorSpaceType.CalGray);
        var (r, g, b) = calGray.ToRgb(new[] { 0.75 });
        r.Should().Be(0.75);
        g.Should().Be(0.75);
        b.Should().Be(0.75);
    }

    [Fact]
    public void FromName_Separation_ReturnsSeparationColorSpace()
    {
        var cs = PdfColorSpace.FromName("Separation");
        cs.Type.Should().Be(PdfColorSpaceType.Separation);
        cs.Components.Should().Be(1);
    }

    [Fact]
    public void DeviceRgb_ExtraComponents_UsesFirst3()
    {
        var cs = PdfColorSpace.DeviceRGB;
        var (r, g, b) = cs.ToRgb(new[] { 0.2, 0.4, 0.6, 0.8, 1.0 });
        r.Should().Be(0.2);
        g.Should().Be(0.4);
        b.Should().Be(0.6);
    }
}
