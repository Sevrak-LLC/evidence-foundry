using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Tests;

public class PromptScaffoldingTests
{
    [Fact]
    public void JoinSections_SkipsEmptySections()
    {
        var result = PromptScaffolding.JoinSections("First section", "", "Second section");

        Assert.Equal("First section\n\nSecond section", result);
    }

    [Fact]
    public void JsonSchemaSection_UsesStandardHeader()
    {
        var schema = "{ \"value\": \"string\" }";

        var section = PromptScaffolding.JsonSchemaSection(schema);

        Assert.Equal($"OUTPUT JSON SCHEMA (EXACT)\n{schema}", section);
    }

    [Fact]
    public void AppendJsonOnlyInstruction_AppendsStandardInstruction()
    {
        var basePrompt = "Base prompt";

        var prompt = PromptScaffolding.AppendJsonOnlyInstruction(basePrompt);

        Assert.Equal($"Base prompt\n\n{PromptScaffolding.JsonOnlyInstruction}", prompt);
    }
}
