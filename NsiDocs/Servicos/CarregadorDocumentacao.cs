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
            for (var indice = 0; indice < secoes.Count; indice++)
            {
                var secao = secoes[indice];
                secao.ProjetoNormalizado = RecuperadorContexto.NormalizarTextoBusca(secao.Projeto);
                secao.TituloNormalizado = RecuperadorContexto.NormalizarTextoBusca(secao.Titulo);
                secao.ConteudoNormalizado = RecuperadorContexto.NormalizarTextoBusca(secao.Conteudo);
                secao.OrdemNoProjeto = indice;
                secao.TokensTitulo = AnaliseTextoBusca.ExtrairTermos(secao.Titulo);
                secao.TokensConteudo = AnaliseTextoBusca.ExtrairTermos(secao.Conteudo);

                var tokensCombinados = new HashSet<string>(secao.TokensTitulo, StringComparer.OrdinalIgnoreCase);
                tokensCombinados.UnionWith(secao.TokensConteudo);
                secao.TokensCombinados = tokensCombinados;
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
