namespace NsiDocs.Configuracoes;

internal sealed class ConfiguracaoAplicacao
{
    public string ModeloOllama { get; init; } = "gpt-oss:120b-cloud";
    public string EndpointOllama { get; init; } = "http://10.14.10.92:11434";
    public string PastaDocumentacoes { get; init; } = string.Empty;
    public TimeSpan TimeoutHttp { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan TimeoutPlanejador { get; init; } = TimeSpan.FromSeconds(45);
    public TimeSpan TimeoutAnalista { get; init; } = TimeSpan.FromSeconds(45);
    public TimeSpan TimeoutRespondedor { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan TimeoutFormatador { get; init; } = TimeSpan.FromSeconds(30);
    public int QuantidadeSecoesRecuperadas { get; init; } = 8;
    public int QuantidadeSecoesUtilizadas { get; init; } = 4;

    public static ConfiguracaoAplicacao Carregar()
    {
        return new ConfiguracaoAplicacao
        {
            ModeloOllama = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "gpt-oss:120b-cloud",
            EndpointOllama = Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT") ?? "http://10.14.10.92:11434",
            PastaDocumentacoes = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "documentacoes"))
        };
    }
}
