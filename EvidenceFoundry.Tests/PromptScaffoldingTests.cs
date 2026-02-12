using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Tests;

public class PromptScaffoldingTests
{
    [Fact]
    public void JoinSectionsSkipsEmptySections()
    {
        var result = PromptScaffolding.JoinSections("First section", "", "Second section");

        Assert.Equal("First section\n\nSecond section", result);
    }

    [Fact]
    public void JsonSchemaSectionUsesStandardHeader()
    {
        var schema = "{ \"value\": \"string\" }";

        var section = PromptScaffolding.JsonSchemaSection(schema);

        Assert.Equal($"OUTPUT JSON SCHEMA (EXACT)\n{schema}", section);
    }

    [Fact]
    public void AppendJsonOnlyInstructionAppendsStandardInstruction()
    {
        var basePrompt = "Base prompt";

        var prompt = PromptScaffolding.AppendJsonOnlyInstruction(basePrompt);

        Assert.Equal($"Base prompt\n\n{PromptScaffolding.JsonOnlyInstruction}", prompt);
    }
}
