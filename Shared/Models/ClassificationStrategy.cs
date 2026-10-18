namespace Exoplanet.Shared.Models;

// Post 1 (Real Thinking Agents): the classifier picks a strategy per iteration.
// Default = fast path, no RAG. WithRag = adds RAG context. WithRagAndDetailedPrompt
// = adds RAG and a more directive prompt. The classifier escalates on retry.
public enum ClassificationStrategy
{
    Default = 0,
    WithRag = 1,
    WithRagAndDetailedPrompt = 2
}
