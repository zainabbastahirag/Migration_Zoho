namespace AGONEAIHub.Infrastructure.Configuration;

public class OpenAISettings
{
    /// <summary>Azure OpenAI endpoint (e.g. https://myopenai.openai.azure.com/)</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Azure OpenAI API key</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Default deployment/model name</summary>
    public string DefaultModel { get; set; } = "gpt-4o";

    /// <summary>Use Azure OpenAI (true) or direct OpenAI (false)</summary>
    public bool UseAzure { get; set; } = true;
}
