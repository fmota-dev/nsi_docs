using System.Text;
using NsiDocs.Modelos;

namespace NsiDocs.Servicos;

internal sealed class RecuperadorContexto
{
    public List<SecaoDocumento> RecuperarSecoes(
        List<ProjetoDocumentacao> projetos,
        PlanoConsulta planoConsulta,
        string pergunta,
        int quantidade)
    {
        var secoes = projetos.SelectMany(projeto => projeto.Secoes).ToList();
        if (secoes.Count == 0)
        {
            return [];
        }

        var consulta = CriarConsultaBusca(planoConsulta, pergunta);
        if (consulta.TermosTodos.Count == 0 && consulta.FrasesPergunta.Count == 0)
        {
            return [];
        }

        var frequencias = CalcularFrequenciasTermos(secoes, consulta.TermosTodos);
        var pontuacoes = secoes
            .Select(secao => new PontuacaoSecao(
                secao,
                CalcularScore(secao, consulta, frequencias, secoes.Count)))
            .ToList();

        AplicarBonusVizinhanca(pontuacoes);

        return pontuacoes
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Secao.Projeto)
            .ThenBy(item => item.Secao.OrdemNoProjeto)
            .Take(quantidade)
            .Select(item => item.Secao)
            .ToList();
    }

    public string MontarContexto(List<SecaoDocumento> secoes)
    {
        var sb = new StringBuilder();
        foreach (var secao in secoes)
        {
            sb.AppendLine($"## PROJETO: {secao.Projeto}");
            sb.AppendLine($"### SECAO: {secao.Titulo}");
            sb.AppendLine($"(fonte: {secao.Arquivo})");
            sb.AppendLine(secao.Conteudo);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static ConsultaBusca CriarConsultaBusca(PlanoConsulta planoConsulta, string pergunta)
    {
        var projetoAlvo = planoConsulta.ProjetoAlvo.Equals("todos", StringComparison.OrdinalIgnoreCase)
            ? null
            : planoConsulta.ProjetoAlvo;
        var termosIgnorados = string.IsNullOrWhiteSpace(projetoAlvo)
            ? null
            : AnaliseTextoBusca.ExtrairTermos(projetoAlvo);

        var termosPergunta = AnaliseTextoBusca.ExtrairTermos(pergunta, termosIgnorados);
        var termosTemas = AnaliseTextoBusca.ExtrairTermos(string.Join(' ', planoConsulta.Temas), termosIgnorados);
        var termosObjetivo = AnaliseTextoBusca.ExtrairTermos(planoConsulta.Objetivo, termosIgnorados);

        var termosTodos = new HashSet<string>(termosPergunta, StringComparer.OrdinalIgnoreCase);
        termosTodos.UnionWith(termosTemas);
        termosTodos.UnionWith(termosObjetivo);

        return new ConsultaBusca(
            termosPergunta,
            termosTemas,
            termosObjetivo,
            termosTodos,
            AnaliseTextoBusca.ExtrairFrasesSignificativas(pergunta, termosIgnorados: termosIgnorados),
            AnaliseTextoBusca.ExtrairFrasesSignificativas(string.Join(' ', planoConsulta.Temas), termosIgnorados: termosIgnorados),
            string.IsNullOrWhiteSpace(projetoAlvo) ? null : NormalizarTextoBusca(projetoAlvo));
    }

    private static Dictionary<string, int> CalcularFrequenciasTermos(
        IReadOnlyCollection<SecaoDocumento> secoes,
        IReadOnlyCollection<string> termosConsulta)
    {
        var frequencias = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var termo in termosConsulta)
        {
            frequencias[termo] = 0;
        }

        foreach (var secao in secoes)
        {
            foreach (var termo in termosConsulta)
            {
                if (secao.TokensCombinados.Contains(termo))
                {
                    frequencias[termo]++;
                }
            }
        }

        return frequencias;
    }

    private static double CalcularScore(
        SecaoDocumento secao,
        ConsultaBusca consulta,
        IReadOnlyDictionary<string, int> frequencias,
        int totalSecoes)
    {
        var score = 0d;
        var projeto = ObterTextoNormalizado(secao.ProjetoNormalizado, secao.Projeto);
        var titulo = ObterTextoNormalizado(secao.TituloNormalizado, secao.Titulo);
        var conteudo = ObterTextoNormalizado(secao.ConteudoNormalizado, secao.Conteudo);

        if (!string.IsNullOrWhiteSpace(consulta.ProjetoAlvoNormalizado) &&
            projeto.Contains(consulta.ProjetoAlvoNormalizado, StringComparison.OrdinalIgnoreCase))
        {
            score += 28d;
        }

        score += PontuarTermos(secao.TokensTitulo, consulta.TermosPergunta, frequencias, totalSecoes, 16d);
        score += PontuarTermos(secao.TokensConteudo, consulta.TermosPergunta, frequencias, totalSecoes, 4.5d);
        score += PontuarTermos(secao.TokensTitulo, consulta.TermosTemas, frequencias, totalSecoes, 10d);
        score += PontuarTermos(secao.TokensConteudo, consulta.TermosTemas, frequencias, totalSecoes, 3.2d);
        score += PontuarTermos(secao.TokensTitulo, consulta.TermosObjetivo, frequencias, totalSecoes, 7d);
        score += PontuarTermos(secao.TokensConteudo, consulta.TermosObjetivo, frequencias, totalSecoes, 2.4d);

        score += PontuarCobertura(secao, consulta);
        score += PontuarFrases(consulta.FrasesPergunta, titulo, conteudo, 18d, 7d);
        score += PontuarFrases(consulta.FrasesTemas, titulo, conteudo, 9d, 3.5d);

        if (EhSecaoEstruturalAmpla(titulo) && score > 0)
        {
            score += 4d;
        }

        if (secao.TokensConteudo.Count > 220)
        {
            score -= Math.Min(8d, (secao.TokensConteudo.Count - 220) / 45d);
        }

        return Math.Round(score, 3);
    }

    private static double PontuarTermos(
        HashSet<string> tokensSecao,
        HashSet<string> termosConsulta,
        IReadOnlyDictionary<string, int> frequencias,
        int totalSecoes,
        double pesoBase)
    {
        var score = 0d;
        foreach (var termo in termosConsulta)
        {
            if (!tokensSecao.Contains(termo))
            {
                continue;
            }

            frequencias.TryGetValue(termo, out var frequencia);
            var idf = 1d + Math.Log((totalSecoes + 1d) / (frequencia + 1d));
            score += pesoBase * idf;
        }

        return score;
    }

    private static double PontuarCobertura(SecaoDocumento secao, ConsultaBusca consulta)
    {
        var totalTermos = consulta.TermosTodos.Count;
        if (totalTermos == 0)
        {
            return 0d;
        }

        var termosCombinados = consulta.TermosTodos.Count(termo => secao.TokensCombinados.Contains(termo));
        var termosTitulo = consulta.TermosTodos.Count(termo => secao.TokensTitulo.Contains(termo));
        var termosPergunta = consulta.TermosPergunta.Count == 0
            ? 0
            : consulta.TermosPergunta.Count(termo => secao.TokensCombinados.Contains(termo));

        var coberturaGeral = (double)termosCombinados / totalTermos;
        var coberturaTitulo = (double)termosTitulo / totalTermos;
        var coberturaPergunta = consulta.TermosPergunta.Count == 0
            ? 0d
            : (double)termosPergunta / consulta.TermosPergunta.Count;

        return (coberturaGeral * 22d) + (coberturaTitulo * 14d) + (coberturaPergunta * 12d);
    }

    private static double PontuarFrases(
        IReadOnlyList<string> frases,
        string tituloNormalizado,
        string conteudoNormalizado,
        double bonusTitulo,
        double bonusConteudo)
    {
        var score = 0d;
        foreach (var frase in frases)
        {
            if (tituloNormalizado.Contains(frase, StringComparison.OrdinalIgnoreCase))
            {
                score += bonusTitulo;
                continue;
            }

            if (conteudoNormalizado.Contains(frase, StringComparison.OrdinalIgnoreCase))
            {
                score += bonusConteudo;
            }
        }

        return score;
    }

    private static void AplicarBonusVizinhanca(List<PontuacaoSecao> pontuacoes)
    {
        var lookup = pontuacoes.ToDictionary(
            item => (Projeto: item.Secao.Projeto, Ordem: item.Secao.OrdemNoProjeto),
            item => item);

        foreach (var item in pontuacoes
                     .Where(item => item.Score >= 24d)
                     .OrderByDescending(item => item.Score)
                     .Take(10))
        {
            foreach (var deslocamento in new[] { -1, 1 })
            {
                if (!lookup.TryGetValue((item.Secao.Projeto, item.Secao.OrdemNoProjeto + deslocamento), out var vizinha))
                {
                    continue;
                }

                if (vizinha.Score <= 0)
                {
                    continue;
                }

                if (!CompartilhaPrefixoEstrutural(item.Secao.Titulo, vizinha.Secao.Titulo))
                {
                    continue;
                }

                vizinha.Score += item.Score >= 42d ? 6d : 3.5d;
            }
        }
    }

    private static bool CompartilhaPrefixoEstrutural(string tituloA, string tituloB)
    {
        var partesA = tituloA.Split(" > ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var partesB = tituloB.Split(" > ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (partesA.Length == 0 || partesB.Length == 0)
        {
            return false;
        }

        return string.Equals(partesA[0], partesB[0], StringComparison.OrdinalIgnoreCase)
            || (partesA.Length > 1
                && partesB.Length > 1
                && string.Equals(partesA[1], partesB[1], StringComparison.OrdinalIgnoreCase));
    }

    private static bool EhSecaoEstruturalAmpla(string tituloNormalizado)
    {
        return tituloNormalizado.Contains("visao geral", StringComparison.OrdinalIgnoreCase)
            || tituloNormalizado.Contains("arquitetura", StringComparison.OrdinalIgnoreCase)
            || tituloNormalizado.Contains("introducao", StringComparison.OrdinalIgnoreCase)
            || tituloNormalizado.Contains("resumo", StringComparison.OrdinalIgnoreCase)
            || tituloNormalizado.Contains("fluxo", StringComparison.OrdinalIgnoreCase)
            || tituloNormalizado.Contains("conceitos", StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizarTextoBusca(string texto)
    {
        return AnaliseTextoBusca.NormalizarTexto(texto);
    }

    private static string ObterTextoNormalizado(string textoNormalizado, string textoOriginal)
    {
        return string.IsNullOrWhiteSpace(textoNormalizado)
            ? NormalizarTextoBusca(textoOriginal)
            : textoNormalizado;
    }

    private sealed class ConsultaBusca
    {
        public ConsultaBusca(
            HashSet<string> termosPergunta,
            HashSet<string> termosTemas,
            HashSet<string> termosObjetivo,
            HashSet<string> termosTodos,
            IReadOnlyList<string> frasesPergunta,
            IReadOnlyList<string> frasesTemas,
            string? projetoAlvoNormalizado)
        {
            TermosPergunta = termosPergunta;
            TermosTemas = termosTemas;
            TermosObjetivo = termosObjetivo;
            TermosTodos = termosTodos;
            FrasesPergunta = frasesPergunta;
            FrasesTemas = frasesTemas;
            ProjetoAlvoNormalizado = projetoAlvoNormalizado;
        }

        public HashSet<string> TermosPergunta { get; }
        public HashSet<string> TermosTemas { get; }
        public HashSet<string> TermosObjetivo { get; }
        public HashSet<string> TermosTodos { get; }
        public IReadOnlyList<string> FrasesPergunta { get; }
        public IReadOnlyList<string> FrasesTemas { get; }
        public string? ProjetoAlvoNormalizado { get; }
    }

    private sealed class PontuacaoSecao
    {
        public PontuacaoSecao(SecaoDocumento secao, double score)
        {
            Secao = secao;
            Score = score;
        }

        public SecaoDocumento Secao { get; }
        public double Score { get; set; }
    }
}
