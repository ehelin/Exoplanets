namespace Exoplanet.Shared.Models;

// How the ClassifierAgent asks the classifier service to do its work.
// The agent picks a different strategy on each loop iteration.
public enum ClassificationStrategy
{
    // Fast pass — no RAG, standard prompt.
    Default,

    // Add scientific context from the RAG vector database.
    WithRag,

    // RAG plus a more explicit / detailed prompt asking for stronger reasoning.
    WithRagAndDetailedPrompt
}
