using System.Text.RegularExpressions;
using NsiDocs.Modelos;

namespace NsiDocs.Servicos;

internal sealed class ParserSecoesMarkdown
{
    public List<SecaoDocumento> ExtrairSecoes(string caminhoArquivo, string conteudo)
    {
        var secoes = new List<SecaoDocumento>();
        var projeto = Path.GetFileNameWithoutExtension(caminhoArquivo);
        var linhas = conteudo.Replace("\r\n", "\n").Split('\n');
        var pilhaTitulos = new Dictionary<int, string>();
        SecaoDocumento? secaoAtual = null;

        foreach (var linha in linhas)
        {
            var match = Regex.Match(linha, @"^(#{2,6})\s+(.*)$");
            if (match.Success)
            {
                if (secaoAtual is not null && !string.IsNullOrWhiteSpace(secaoAtual.Conteudo))
                {
                    secaoAtual.Conteudo = secaoAtual.Conteudo.Trim();
                    secoes.Add(secaoAtual);
                }

                var nivel = match.Groups[1].Value.Length;
                var titulo = match.Groups[2].Value.Trim();
                foreach (var chave in pilhaTitulos.Keys.Where(chave => chave >= nivel).ToList())
                {
                    pilhaTitulos.Remove(chave);
                }

                pilhaTitulos[nivel] = titulo;
                var trilha = string.Join(" > ",
                    pilhaTitulos.OrderBy(item => item.Key).Select(item => item.Value));

                secaoAtual = new SecaoDocumento
                {
                    Projeto = projeto,
                    Arquivo = Path.GetFileName(caminhoArquivo),
                    Titulo = trilha,
                    Conteudo = linha + "\n"
                };

                continue;
            }

            if (secaoAtual is not null)
            {
                secaoAtual.Conteudo += linha + "\n";
            }
        }

        if (secaoAtual is not null && !string.IsNullOrWhiteSpace(secaoAtual.Conteudo))
        {
            secaoAtual.Conteudo = secaoAtual.Conteudo.Trim();
            secoes.Add(secaoAtual);
        }

        return secoes;
    }
}
