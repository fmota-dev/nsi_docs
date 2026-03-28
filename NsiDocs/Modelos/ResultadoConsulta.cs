namespace NsiDocs.Modelos;

internal sealed class ResultadoConsulta
{
    public string RespostaFinal { get; init; } = string.Empty;
    public List<SecaoDocumento> SecoesUtilizadas { get; init; } = [];
}
