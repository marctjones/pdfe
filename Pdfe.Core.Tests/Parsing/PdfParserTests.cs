using FluentAssertions;
using Pdfe.Core.Parsing;
using Pdfe.Core.Primitives;
using System.Text;
using Xunit;

namespace Pdfe.Core.Tests.Parsing;

public class PdfParserTests
{
    [Fact]
    public void ParseObject_Integer_ReturnsPdfInteger()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("123"));

        var obj = parser.ParseObject();

        obj.Should().BeOfType<PdfInteger>();
        ((PdfInteger)obj).Value.Should().Be(123);
    }

    [Fact]
    public void ParseObject_NegativeInteger_ReturnsPdfInteger()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("-45"));

        var obj = parser.ParseObject();

        obj.Should().BeOfType<PdfInteger>();
        ((PdfInteger)obj).Value.Should().Be(-45);
    }

    [Fact]
    public void ParseObject_Real_ReturnsPdfReal()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("3.14159"));

        var obj = parser.ParseObject();

        obj.Should().BeOfType<PdfReal>();
        ((PdfReal)obj).Value.Should().BeApproximately(3.14159, 0.00001);
    }

    [Fact]
    public void ParseObject_True_ReturnsPdfBooleanTrue()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("true"));

        var obj = parser.ParseObject();

        obj.Should().Be(PdfBoolean.True);
    }

    [Fact]
    public void ParseObject_False_ReturnsPdfBooleanFalse()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("false"));

        var obj = parser.ParseObject();

        obj.Should().Be(PdfBoolean.False);
    }

    [Fact]
    public void ParseObject_Null_ReturnsPdfNull()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("null"));

        var obj = parser.ParseObject();

        obj.Should().Be(PdfNull.Instance);
    }

    [Fact]
    public void ParseObject_Name_ReturnsPdfName()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("/Type"));

        var obj = parser.ParseObject();

        obj.Should().BeOfType<PdfName>();
        ((PdfName)obj).Value.Should().Be("Type");
    }

    [Fact]
    public void ParseObject_LiteralString_ReturnsPdfString()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("(Hello World)"));

        var obj = parser.ParseObject();

        obj.Should().BeOfType<PdfString>();
        ((PdfString)obj).Value.Should().Be("Hello World");
    }

    [Fact]
    public void ParseObject_HexString_ReturnsPdfString()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("<48656C6C6F>")); // "Hello"

        var obj = parser.ParseObject();

        obj.Should().BeOfType<PdfString>();
        ((PdfString)obj).Value.Should().Be("Hello");
    }

    [Fact]
    public void ParseObject_Array_ReturnsPdfArray()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("[1 2 3]"));

        var obj = parser.ParseObject();

        obj.Should().BeOfType<PdfArray>();
        var arr = (PdfArray)obj;
        arr.Count.Should().Be(3);
        arr.GetInt(0).Should().Be(1);
        arr.GetInt(1).Should().Be(2);
        arr.GetInt(2).Should().Be(3);
    }

    [Fact]
    public void ParseObject_NestedArray_ReturnsPdfArray()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("[[1 2] [3 4]]"));

        var obj = parser.ParseObject();

        obj.Should().BeOfType<PdfArray>();
        var arr = (PdfArray)obj;
        arr.Count.Should().Be(2);
        arr[0].Should().BeOfType<PdfArray>();
        arr[1].Should().BeOfType<PdfArray>();
    }

    [Fact]
    public void ParseObject_Dictionary_ReturnsPdfDictionary()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("<< /Type /Page /Count 10 >>"));

        var obj = parser.ParseObject();

        obj.Should().BeOfType<PdfDictionary>();
        var dict = (PdfDictionary)obj;
        dict.GetName("Type").Should().Be("Page");
        dict.GetInt("Count").Should().Be(10);
    }

    [Fact]
    public void ParseObject_NestedDictionary_ReturnsPdfDictionary()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("<< /Outer << /Inner /Value >> >>"));

        var obj = parser.ParseObject();

        obj.Should().BeOfType<PdfDictionary>();
        var dict = (PdfDictionary)obj;
        var inner = dict.GetDictionary("Outer");
        inner.GetName("Inner").Should().Be("Value");
    }

    [Fact]
    public void ParseObject_Reference_ReturnsPdfReference()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("5 0 R"));

        var obj = parser.ParseObject();

        obj.Should().BeOfType<PdfReference>();
        var reference = (PdfReference)obj;
        reference.ObjectNum.Should().Be(5);
        reference.Generation.Should().Be(0);
    }

    [Fact]
    public void ParseObject_MixedArray_ParsesCorrectly()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("[1 3.14 /Name (string) true null]"));

        var obj = parser.ParseObject();

        obj.Should().BeOfType<PdfArray>();
        var arr = (PdfArray)obj;
        arr.Count.Should().Be(6);
        arr[0].Should().BeOfType<PdfInteger>();
        arr[1].Should().BeOfType<PdfReal>();
        arr[2].Should().BeOfType<PdfName>();
        arr[3].Should().BeOfType<PdfString>();
        arr[4].Should().Be(PdfBoolean.True);
        arr[5].Should().Be(PdfNull.Instance);
    }

    [Fact]
    public void ParseIndirectObject_SimpleObject_ParsesCorrectly()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("5 0 obj\n123\nendobj"));

        var indObj = parser.ParseIndirectObject();

        indObj.ObjectNumber.Should().Be(5);
        indObj.Generation.Should().Be(0);
        indObj.Value.Should().BeOfType<PdfInteger>();
        ((PdfInteger)indObj.Value).Value.Should().Be(123);
    }

    [Fact]
    public void ParseIndirectObject_Dictionary_ParsesCorrectly()
    {
        using var parser = new PdfParser(Encoding.ASCII.GetBytes("1 0 obj\n<< /Type /Page >>\nendobj"));

        var indObj = parser.ParseIndirectObject();

        indObj.ObjectNumber.Should().Be(1);
        indObj.Value.Should().BeOfType<PdfDictionary>();
    }

    [Fact]
    public void ParseIndirectObject_Stream_ParsesCorrectly()
    {
        var streamContent = "Hello World";
        var pdfData = $"1 0 obj\n<< /Length {streamContent.Length} >>\nstream\n{streamContent}\nendstream\nendobj";
        using var parser = new PdfParser(Encoding.ASCII.GetBytes(pdfData));

        var indObj = parser.ParseIndirectObject();

        indObj.Value.Should().BeOfType<PdfStream>();
        var stream = (PdfStream)indObj.Value;
        stream.EncodedData.Should().BeEquivalentTo(Encoding.ASCII.GetBytes(streamContent));
    }
}
