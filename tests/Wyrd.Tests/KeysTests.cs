using System.Globalization;
using Wyrd.Runtime;

namespace Wyrd.Tests;

public class KeysTests
{
    private sealed record OrderId(string Value)
    {
        public override string ToString() => Value;
    }

    private sealed class Nameless
    {
        public override string? ToString() => null;
    }

    [Fact]
    public void a_key_is_the_same_string_whatever_the_culture_of_the_machine()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); // a decimal comma
            Assert.Equal("A-1", Keys.Of("A-1"));
            Assert.Equal("1234.5", Keys.Of(1234.5m));
            Assert.Equal("42", Keys.Of(42));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void a_value_object_key_is_its_text()
    {
        Assert.Equal("A-1", Keys.Of(new OrderId("A-1")));
    }

    [Fact]
    public void a_key_with_no_text_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => Keys.Of(new Nameless()));
    }
}
