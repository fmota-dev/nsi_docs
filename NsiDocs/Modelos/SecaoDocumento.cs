namespace NsiDocs.Modelos;

internal sealed class SecaoDocumento
{
    public string Projeto { get; set; } = string.Empty;
    public string Arquivo { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Conteudo { get; set; } = string.Empty;
    public string ProjetoNormalizado { get; set; } = string.Empty;
    public string TituloNormalizado { get; set; } = string.Empty;
    public string ConteudoNormalizado { get; set; } = string.Empty;
}
