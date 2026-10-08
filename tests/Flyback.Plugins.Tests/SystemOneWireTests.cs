using Flyback.Plugins.Decide;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>The System One format, read and written without a network.</summary>
public class SystemOneWireTests
{
    private const string Asked = """
        {"state": "we were billed twice", "model": "english",
         "questions": {"urgent": {"type": "noul", "instructions": "Does this message express urgency?"},
                       "dept":   {"type": "choice", "instructions": "Which department?", "criteria": {"billing": "Money", "other": "Anything else"}},
                       "level":  {"type": "score", "instructions": "How loud?", "criteria": ["quiet", "medium", "loud"]}}}
        """;

    private const string Answered = """
        {"model": "laya-rl-agent",
         "answers": {"urgent": {"type": "noul", "noul": 0.97},
                     "dept":   {"type": "choice", "choice": "billing", "probabilities": {"billing": 0.9, "other": 0.1}, "confidence": 0.8},
                     "level":  {"type": "score", "score": 1.7, "legend": {"0": "quiet", "1": "medium", "2": "loud"}, "probabilities": {"0": 0.1, "1": 0.2, "2": 0.7}, "confidence": 0.6}},
         "usage": {"input_tokens": 392, "output_tokens": 0}}
        """;

    [Fact]
    public void A_request_reads_back_as_the_questions_it_asked()
    {
        var questions = SystemOneWire.ReadQuestions(Asked, out var problem).ShouldNotBeNull();
        problem.ShouldBeNull();

        questions["urgent"].ShouldBe(new Question.YesNo("Does this message express urgency?"));
        questions["dept"].ShouldBeOfType<Question.Choice>().Options.ShouldBe(
            [new ChoiceOption("billing", "Money"), new ChoiceOption("other", "Anything else")]);
        questions["level"].ShouldBeOfType<Question.Score>().Levels.ShouldBe(["quiet", "medium", "loud"]);
    }

    [Fact]
    public void A_request_written_reads_back_the_same()
    {
        var questions = SystemOneWire.ReadQuestions(Asked, out _)!;
        var written = SystemOneWire.Request(new DecisionRequest("we were billed twice", questions), "english");

        var again = SystemOneWire.ReadQuestions(written, out var problem).ShouldNotBeNull();

        problem.ShouldBeNull();
        written.ShouldContain("\"state\":\"we were billed twice\"");
        written.ShouldContain("\"model\":\"english\"");
        again.Keys.ShouldBe(questions.Keys);
        again["dept"].ShouldBeOfType<Question.Choice>().Options.ShouldBe(((Question.Choice)questions["dept"]).Options);
    }

    [Fact]
    public void An_answer_reads_as_one_answer_per_question()
    {
        var decision = SystemOneWire.ReadDecision(Answered, out var problem).ShouldNotBeNull();
        problem.ShouldBeNull();

        decision.Model.ShouldBe("laya-rl-agent");
        decision.Usage.ShouldBe(new DecisionUsage(392, 0));
        decision.Answers["urgent"].ShouldBe(new Answer.YesNo(0.97));

        var chosen = decision.Answers["dept"].ShouldBeOfType<Answer.Chosen>();
        chosen.Option.ShouldBe("billing");
        chosen.Probabilities["other"].ShouldBe(0.1);
        chosen.Confidence.ShouldBe(0.8);

        var scored = decision.Answers["level"].ShouldBeOfType<Answer.Scored>();
        scored.Score.ShouldBe(1.7);
        scored.Levels.ShouldBe(["quiet", "medium", "loud"]);
        scored.Probabilities.ShouldBe([0.1, 0.2, 0.7]);
    }

    [Fact]
    public void An_answer_written_reads_back_the_same()
    {
        var decision = SystemOneWire.ReadDecision(Answered, out _)!;

        var again = SystemOneWire.ReadDecision(SystemOneWire.Answer(decision), out var problem).ShouldNotBeNull();

        problem.ShouldBeNull();
        again.Model.ShouldBe(decision.Model);
        again.Usage.ShouldBe(decision.Usage);
        again.Answers["urgent"].ShouldBe(decision.Answers["urgent"]);
        again.Answers["level"].ShouldBeOfType<Answer.Scored>().Probabilities.ShouldBe([0.1, 0.2, 0.7]);
        again.Answers["dept"].ShouldBeOfType<Answer.Chosen>().Probabilities.ShouldBe(
            ((Answer.Chosen)decision.Answers["dept"]).Probabilities);
    }

    [Theory]
    [InlineData("", "not JSON")]
    [InlineData("[]", "not a JSON object")]
    [InlineData("""{"model":"x"}""", "no answers")]
    [InlineData("""{"answers":{"a":{"type":"noul"}}}""", "'a'")]
    [InlineData("""{"answers":{"a":{"type":"guess","guess":1}}}""", "'a'")]
    [InlineData("""{"answers":{},"answers":{}}""", "not JSON")]
    public void An_answer_that_is_not_one_says_why_rather_than_throwing(string json, string said)
    {
        SystemOneWire.ReadDecision(json, out var problem).ShouldBeNull();

        problem.ShouldNotBeNull().ShouldContain(said);
    }

    [Theory]
    [InlineData("""{"q":{"type":"choice","instructions":"Which?","criteria":["a","b"]}}""")]
    [InlineData("""{"q":{"type":"score","instructions":"How?","criteria":{"a":"b"}}}""")]
    [InlineData("""{"q":{"type":"noul"}}""")]
    [InlineData("""{"q":"Is it?"}""")]
    public void Questions_that_are_not_any_kind_are_refused_with_their_id(string json)
    {
        SystemOneWire.ReadQuestions(json, out var problem).ShouldBeNull();

        problem.ShouldNotBeNull().ShouldContain("'q'");
    }

    [Fact]
    public void A_request_too_big_or_too_vague_is_refused_before_it_is_sent()
    {
        DecisionRequest.One(new string('x', DecisionRequest.LongestState + 1), "q", new Question.YesNo("Is it?"))
            .Problem().ShouldNotBeNull().ShouldContain("characters");

        new DecisionRequest("s", Enumerable.Range(0, DecisionRequest.MostQuestions + 1)
                .ToDictionary(i => $"q{i}", Question (_) => new Question.YesNo("Is it?")))
            .Problem().ShouldNotBeNull().ShouldContain("questions");

        DecisionRequest.One("s", "q", new Question.Choice("Which?", [new ChoiceOption("only", "")]))
            .Problem().ShouldNotBeNull().ShouldContain("fewer than two");

        DecisionRequest.One("s", "q", new Question.Choice("Which?", [new ChoiceOption("a", ""), new ChoiceOption("a", "")]))
            .Problem().ShouldNotBeNull().ShouldContain("same one");

        DecisionRequest.One("s", "q", new Question.YesNo("Is it?")).Problem().ShouldBeNull();
    }
}
