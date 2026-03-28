namespace NsiDocs.Modelos;

internal sealed class PlanoConsulta
{
    public string ProjetoAlvo { get; init; } = "todos";
    public List<string> Temas { get; init; } = [];
    public string Objetivo { get; init; } = string.Empty;
}
