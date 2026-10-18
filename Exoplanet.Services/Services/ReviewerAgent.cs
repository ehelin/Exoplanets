using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Exoplanet.Shared.Interfaces;
using Microsoft.Extensions.Configuration;
using Shared.Interfaces;

namespace Exoplanet.Services.Services;

// Post 1 (Real Thinking Agents): independent second opinion on the classifier's work.
// Makes its own LLM call, decides authorize | escalate | reject, and sits between
// the classifier and the narrator in the pipeline.
public sealed class ReviewerAgent : IAgent
{
    private readonly HttpClient _http;
    private readonly IAgentBoundary _boundary;
    private readonly IPipelineLogger _plog;
    private readonly string _model;

    public string AgentName => "reviewer";
    public int MaxIterations => 3;

    public ReviewerAgent(
        HttpClient http,
        IAgentBoundary boundary,
        IPipelineLogger plog,
        IConfiguration config)
    {
        _http = http;
        _boundary = boundary;
        _plog = plog;
        _model = config["OpenAI:Model"] ?? "gpt-4o-mini";

        var apiKey = config["OpenAI:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public async Task ExecuteAsync(int ingestRunId)
    {
        // What the agent received
        var classifierHandoff = await _boundary.ReadPreviousHandoffAsync(ingestRunId, "classifier");
        if (classifierHandoff == null)
        {
            await _plog.Warning(
                $"{AgentName} agent: no classifier handoff for run {ingestRunId}, skipping.",
                ingestRunId);
            return;
        }

        var inputSummary =
            $"classifier produced {EstimateClassificationCount(classifierHandoff.OutputSummary)} items";

        // Do the job — self-directed loop asking the LLM for a second opinion.
        // Escalate review depth each iteration if the model keeps saying "escalate".
        string verdict = "no_attempt";
        string reasoning = string.Empty;

        for (int iteration = 1; iteration <= MaxIterations; iteration++)
        {
            var depth = ChooseReviewDepth(iteration);
            await _plog.Info(
                $"{AgentName} iteration {iteration}/{MaxIterations} using depth={depth}",
                ingestRunId);

            (verdict, reasoning) = await ReviewAsync(classifierHandoff.OutputSummary, depth);

            await _boundary.RecordIterationAsync(
                ingestRunId,
                AgentName,
                iteration,
                depth,
                $"verdict={verdict}; reasoning={Truncate(reasoning, 200)}",
                verdict == "escalate" ? "retry_uncertain" : "satisfied");

            if (verdict != "escalate")
                break;

            if (iteration == MaxIterations)
            {
                await _plog.Warning(
                    $"{AgentName} still uncertain after {MaxIterations} iterations, defaulting to authorize.",
                    ingestRunId);
                verdict = "authorize";
            }
        }

        // What the agent produced
        var outputSummary = JsonSerializer.Serialize(new { verdict, reasoning });

        // Log the boundary crossing
        await _boundary.RecordHandoffAsync(
            ingestRunId,
            AgentName,
            inputSummary,
            outputSummary);

        await _plog.Info($"{AgentName} agent complete. Verdict={verdict}", ingestRunId);
    }

    private static string ChooseReviewDepth(int iteration) => iteration switch
    {
        1 => "quick-scan",
        2 => "detailed-review",
        _ => "deep-audit"
    };

    private static int EstimateClassificationCount(string outputSummary)
    {
        try
        {
            using var doc = JsonDocument.Parse(outputSummary);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.GetArrayLength()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<(string verdict, string reasoning)> ReviewAsync(string classifierOutput, string depth)
    {
        var prompt =
            "You are an independent AI auditor. Review the following exoplanet classifications " +
            "produced by another AI classifier. Your job: judge whether they look trustworthy.\n\n" +
            $"Depth of review requested: {depth}.\n\n" +
            $"Classifications:\n{classifierOutput}\n\n" +
            "Reply with a single JSON object only, no prose: " +
            "{\"verdict\":\"authorize|escalate|reject\",\"reasoning\":\"...\"}\n" +
            "- authorize: classifications look reasonable; ship them.\n" +
            "- escalate: you are not confident; the next iteration should look harder.\n" +
            "- reject: clear problems; do not ship.";

        var requestBody = new
        {
            model = _model,
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = 500,
            temperature = 0.2
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("https://api.openai.com/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        return ParseVerdict(text);
    }

    private static (string verdict, string reasoning) ParseVerdict(string raw)
    {
        try
        {
            var cleaned = raw.Trim();
            if (cleaned.StartsWith("```"))
                cleaned = cleaned.Replace("```json", "").Replace("```", "").Trim();

            using var doc = JsonDocument.Parse(cleaned);
            var verdict = doc.RootElement.GetProperty("verdict").GetString() ?? "escalate";
            var reasoning = doc.RootElement.TryGetProperty("reasoning", out var r)
                ? (r.GetString() ?? "")
                : "";
            return (verdict, reasoning);
        }
        catch
        {
            return ("escalate", $"parse failed: {raw}");
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "...";
}
