using NsiDocs.Modelos;

namespace NsiDocs.Servicos;

internal sealed class CarregadorDocumentacao(ParserSecoesMarkdown parserSecoesMarkdown)
{
    public async Task<List<ProjetoDocumentacao>> CarregarProjetosAsync(string pastaDocumentacoes)
    {
        if (!Directory.Exists(pastaDocumentacoes))
        {
            throw new DirectoryNotFoundException($"Pasta de documentacoes nao encontrada: {pastaDocumentacoes}");
        }

        var arquivos = Directory.GetFiles(pastaDocumentacoes, "*.md", SearchOption.AllDirectories);
        var projetos = new List<ProjetoDocumentacao>();

        foreach (var arquivo in arquivos)
        {
            var conteudo = await File.ReadAllTextAsync(arquivo);
            if (string.IsNullOrWhiteSpace(conteudo))
            {
                continue;
            }

            var nomeProjeto = Path.GetFileNameWithoutExtension(arquivo);
            var identificador = Path
                .GetRelativePath(pastaDocumentacoes, arquivo)
                .Replace('\\', '/');
            var secoes = parserSecoesMarkdown.ExtrairSecoes(arquivo, conteudo);
            foreach (var secao in secoes)
            {
                secao.ProjetoNormalizado = RecuperadorContexto.NormalizarTextoBusca(secao.Projeto);
                secao.TituloNormalizado = RecuperadorContexto.NormalizarTextoBusca(secao.Titulo);
                secao.ConteudoNormalizado = RecuperadorContexto.NormalizarTextoBusca(secao.Conteudo);
            }

            projetos.Add(new ProjetoDocumentacao
            {
                Identificador = identificador,
                Nome = nomeProjeto,
                Arquivo = Path.GetFileName(arquivo),
                Secoes = secoes
            });
        }

        return projetos;
    }
}
