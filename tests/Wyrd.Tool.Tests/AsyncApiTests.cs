using Wyrd.Tool;

namespace Wyrd.Tool.Tests;

public class AsyncApiTests
{
    [Theory]
    [InlineData("string", "type: string")]
    [InlineData("int", "type: integer")]
    [InlineData("long", "type: integer")]
    [InlineData("short", "type: integer")]
    [InlineData("byte", "type: integer")]
    [InlineData("sbyte", "type: integer")]
    [InlineData("ushort", "type: integer")]
    [InlineData("uint", "type: integer")]
    [InlineData("ulong", "type: integer")]
    [InlineData("decimal", "type: number")]
    [InlineData("double", "type: number")]
    [InlineData("float", "type: number")]
    [InlineData("bool", "type: boolean")]
    [InlineData("Guid", "type: string\n            format: uuid")]
    [InlineData("DateOnly", "type: string\n            format: date")]
    [InlineData("DateTime", "type: string\n            format: date-time")]
    [InlineData("DateTimeOffset", "type: string\n            format: date-time")]
    [InlineData("TimeSpan", "type: string\n            format: duration")]
    [InlineData("IReadOnlyList<Guid>", "type: array\n            items:\n              type: string\n              format: uuid")]
    [InlineData("List<int>", "type: array\n            items:\n              type: integer")]
    [InlineData("string[]", "type: array\n            items:\n              type: string")]
    [InlineData("IReadOnlyList<string?>", "type: array\n            items:\n              type: string")]
    [InlineData("ImmutableList<string?>", "type: array\n            items:\n              type: string")]
    [InlineData("ImmutableArray<uint>", "type: array\n            items:\n              type: integer")]
    [InlineData("IReadOnlyCollection<Guid?>", "type: array\n            items:\n              type: string\n              format: uuid")]
    [InlineData("CustomThing", "type: object\n            description: \"CustomThing\"")]
    public void maps_snapshot_types_to_schema(string type, string expected)
    {
        var document = AsyncApi.Generate(Contracts.Parse($"example.done v1 | Value: {type}"), "Example", "1.0.0");

        Assert.Contains($"\"Value\":\n            {expected}", document, StringComparison.Ordinal);
        Assert.Contains("title: \"Example\"", document, StringComparison.Ordinal);
        Assert.Contains("version: \"1.0.0\"", document, StringComparison.Ordinal);
    }

    [Fact]
    public void optional_fields_are_properties_but_not_required()
    {
        var document = AsyncApi.Generate(Contracts.Parse("example.done v1 | Required: string; Optional: Guid?"), "x", "1");

        Assert.Contains("\"Optional\":\n            type: string\n            format: uuid", document, StringComparison.Ordinal);
        Assert.Contains("required:\n          - \"Required\"", document, StringComparison.Ordinal);
        Assert.DoesNotContain("- \"Optional\"", document, StringComparison.Ordinal);
    }

    [Fact]
    public void retired_versions_keep_their_channel_and_are_deprecated()
    {
        var document = AsyncApi.Generate(Contracts.Parse("retired example.done v1 | A: int\nexample.done v2 | A: int"), "x", "1");

        Assert.Equal(1, document.Split("address: \"example.done\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("\"v1\":", document, StringComparison.Ordinal);
        Assert.Contains("\"v2\":", document, StringComparison.Ordinal);
        Assert.Equal(1, document.Split("deprecated: true", StringSplitOptions.None).Length - 1);
        Assert.Contains("\"example.done.v1\":\n      name: \"example.done.v1\"\n      payload:\n        type: object\n        deprecated: true", document, StringComparison.Ordinal);
    }

    [Fact]
    public void ignores_comments_and_blank_lines_and_orders_contracts_and_fields()
    {
        var first = "# comment\n\nz.done v2 | Z: string; A: bool\na.done v1 | B: int";
        var second = "a.done v1 | B: int\n# another\nz.done v2 | A: bool; Z: string\n";
        var a = AsyncApi.Generate(Contracts.Parse(first), "x", "1");
        var b = AsyncApi.Generate(Contracts.Parse(second), "x", "1");

        Assert.Equal(a, b);
        Assert.True(a.IndexOf("address: \"a.done\"", StringComparison.Ordinal)
            < a.IndexOf("address: \"z.done\"", StringComparison.Ordinal));
        Assert.True(a.IndexOf("\"A\":", StringComparison.Ordinal) < a.IndexOf("\"Z\":", StringComparison.Ordinal));
    }

    [Fact]
    public void malformed_snapshot_reports_its_line()
    {
        var error = Assert.Throws<FormatException>(() => Contracts.Parse("# comment\n\nwrong line"));

        Assert.Contains("line 3", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void nested_nullable_collections_preserve_the_element_type()
    {
        var schema = SchemaType.Parse("ImmutableArray<IReadOnlyList<string?>?>?");

        Assert.Equal("array", schema.Type);
        Assert.Equal("array", schema.Items!.Type);
        Assert.Equal(new SchemaType("string"), schema.Items.Items);
    }

    [Fact]
    public void unknown_generic_type_descriptions_keep_angle_brackets()
    {
        var yaml = AsyncApi.Generate(Contracts.Parse("example.done v1 | Value: Dictionary<string, int>"), "x", "1");

        Assert.Contains("description: \"Dictionary<string, int>\"", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u003", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void sample_document_is_unchanged_by_the_serializer_refactor()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Samples");
        var contracts = Contracts.Parse(File.ReadAllText(Path.Combine(directory, "contracts.wyrd.txt")));
        var expected = File.ReadAllText(Path.Combine(directory, "asyncapi.yaml")).ReplaceLineEndings("\n");

        Assert.Equal(expected, AsyncApi.Generate(contracts, "Shop", "0.0.0"));
    }
}
