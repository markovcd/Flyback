using System.Text.Json;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

public sealed class LayaContractTests
{
    [Fact]
    public void A_request_carries_structured_state_and_typed_questions()
    {
        using var document = JsonDocument.Parse("""{"diagnostic":"missing-close"}""");
        var request = new LayaRequest(
            document.RootElement.Clone(),
            [
                new LayaQuestion.Choice(
                    "cause",
                    "Which cause best fits the diagnostic?",
                    new Dictionary<string, string>
                    {
                        ["missing_delimiter"] = "A closing delimiter is missing.",
                        ["unclear"] = "The evidence is inconclusive.",
                    }),
                new LayaQuestion.Noul("safe_to_retry", "Is a retry safe?"),
            ]);

        request.State.GetProperty("diagnostic").GetString().ShouldBe("missing-close");
        request.Questions.Select(question => question.Id).ShouldBe(["cause", "safe_to_retry"]);
        request.Questions[0].ShouldBeOfType<LayaQuestion.Choice>().Criteria.ShouldContainKey("unclear");
    }

    [Fact]
    public void An_installed_model_exposes_its_onnx_path()
    {
        var definition = new LayaModelDefinition(
            "english",
            "Laya English",
            "1.0",
            "onnx/laya.onnx",
            [
                new LayaModelArtifact(
                    "onnx/laya.onnx",
                    new Uri("https://models.example/laya.onnx"),
                    new string('a', 64)),
            ]);
        var installed = new InstalledLayaModel(definition, Path.Combine("models", "english"));

        installed.OnnxPath.ShouldBe(Path.Combine("models", "english", "onnx", "laya.onnx"));
    }
}
