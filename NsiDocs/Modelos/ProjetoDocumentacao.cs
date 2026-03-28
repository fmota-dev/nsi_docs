namespace AgentesFramework.Modelos;

internal sealed class ProjetoDocumentacao
{
    public string Nome { get; init; } = string.Empty;
    public string Arquivo { get; init; } = string.Empty;
    public List<SecaoDocumento> Secoes { get; init; } = [];
}
