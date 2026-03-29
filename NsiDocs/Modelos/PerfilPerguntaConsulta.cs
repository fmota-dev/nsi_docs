using System.Globalization;
using System.Text;

namespace NsiDocs.Modelos;

internal enum PerfilPerguntaConsulta
{
    Factual,
    Explicativa,
    Comparativa,
    Geral
}

internal static class PerfilPerguntaConsultaHelper
{
    public static PerfilPerguntaConsulta Classificar(string pergunta)
    {
        var texto = Normalizar(pergunta);

        if (ContemQualquer(texto, "compare", "comparar", "diferenca", "diferencas", "versus", "vs "))
        {
            return PerfilPerguntaConsulta.Comparativa;
        }

        if (ContemQualquer(texto, "explique", "detalhe", "detalhar", "como funciona", "descreva"))
        {
            return PerfilPerguntaConsulta.Explicativa;
        }

        if (ContemQualquer(
            texto,
            "qual ",
            "quais ",
            "que autenticacao",
            "qual autenticacao",
            "que stack",
            "qual stack",
            "liste",
            "listar",
            "lista "))
        {
            return PerfilPerguntaConsulta.Factual;
        }

        return PerfilPerguntaConsulta.Geral;
    }

    public static string DescreverContratoResposta(this PerfilPerguntaConsulta perfil)
    {
        return perfil switch
        {
            PerfilPerguntaConsulta.Factual =>
                """
                Perfil factual.
                Contrato obrigatorio:
                - A primeira linha deve responder diretamente a pergunta.
                - Se o contexto tiver a resposta, nunca use fallback.
                - Depois da resposta direta, detalhe apenas fatos sustentados pelo contexto.
                - Cite termos tecnicos centrais quando eles forem essenciais para a resposta.
                - Nao devolva fragmentos soltos sem explicar o que eles representam.
                - Preserve a mesma conclusao factual em qualquer modo de resposta.
                """,
            PerfilPerguntaConsulta.Comparativa =>
                """
                Perfil comparativo.
                Contrato obrigatorio:
                - Compare explicitamente os itens citados.
                - Separe semelhancas e diferencas de forma clara.
                - Nao invente comparacoes fora do contexto recuperado.
                """,
            PerfilPerguntaConsulta.Explicativa =>
                """
                Perfil explicativo.
                Contrato obrigatorio:
                - Abra com um resumo objetivo do tema.
                - Se o contexto identificar o mecanismo principal, cite-o ja no resumo.
                - Em seguida, explique o fluxo ou os pontos principais em ordem logica.
                - Preserve a mesma base factual entre os modos de resposta.
                - Nao use fallback se o contexto permitir uma explicacao parcial util; explicite os limites.
                - Nao use expressoes especulativas como "geralmente", "por exemplo", "pode ser" ou alternativas nao sustentadas pelo contexto.
                - Quando um detalhe nao estiver documentado, diga claramente que ele nao aparece no contexto recuperado.
                - Quando houver termos tecnicos centrais relevantes, inclua-os explicitamente.
                """,
            _ =>
                """
                Perfil geral.
                Contrato obrigatorio:
                - Responda de forma objetiva e organizada.
                - Abra pelo ponto principal da pergunta.
                - Use apenas fatos presentes no contexto recuperado.
                """
        };
    }

    public static string DescreverMoldeResposta(this PerfilPerguntaConsulta perfil, ModoRespostaConsulta modo)
    {
        return (perfil, modo) switch
        {
            (PerfilPerguntaConsulta.Factual, ModoRespostaConsulta.Curta) =>
                """
                Molde esperado:
                Resposta direta: [uma frase objetiva]
                - [fato tecnico 1]
                - [fato tecnico 2]
                """,
            (PerfilPerguntaConsulta.Factual, ModoRespostaConsulta.Detalhada) =>
                """
                Molde esperado:
                Resposta direta: [uma frase objetiva]
                Detalhes:
                - [fato tecnico 1]
                - [fato tecnico 2]
                - [fato tecnico 3]
                - [fato tecnico 4]
                Observacoes:
                - [opcional, apenas se estiver no contexto]
                """,
            (PerfilPerguntaConsulta.Factual, _) =>
                """
                Molde esperado:
                Resposta direta: [uma frase objetiva]
                Detalhes:
                - [fato tecnico 1]
                - [fato tecnico 2]
                - [fato tecnico 3]
                """,
            (PerfilPerguntaConsulta.Comparativa, _) =>
                """
                Molde esperado:
                Resumo: [comparacao curta]
                Semelhancas:
                - [...]
                Diferencas:
                - [...]
                """,
            (PerfilPerguntaConsulta.Explicativa, ModoRespostaConsulta.Curta) =>
                """
                Molde esperado:
                Resumo: [visao geral objetiva com o mecanismo principal]
                Fluxo ou pontos principais:
                - [passo ou regra documentada]
                - [passo ou regra documentada]
                Pontos tecnicos:
                - [termo tecnico central documentado]
                """,
            (PerfilPerguntaConsulta.Explicativa, ModoRespostaConsulta.Detalhada) =>
                """
                Molde esperado:
                Resumo: [visao geral objetiva com o mecanismo principal]
                Fluxo ou pontos principais:
                - [passo ou regra documentada]
                - [passo ou regra documentada]
                - [passo ou regra documentada]
                Pontos tecnicos:
                - [termo tecnico central documentado]
                Limites do contexto:
                - [o que nao foi detalhado na documentacao]
                """,
            (PerfilPerguntaConsulta.Explicativa, _) =>
                """
                Molde esperado:
                Resumo: [visao geral objetiva com o mecanismo principal]
                Fluxo ou pontos principais:
                - [passo ou regra documentada]
                - [passo ou regra documentada]
                - [passo ou regra documentada]
                Pontos tecnicos:
                - [termo tecnico central documentado]
                """,
            _ =>
                """
                Molde esperado:
                Resposta principal: [ponto central]
                Detalhes:
                - [...]
                """
        };
    }

    private static bool ContemQualquer(string texto, params string[] termos)
    {
        return termos.Any(termo => texto.Contains(termo, StringComparison.Ordinal));
    }

    private static string Normalizar(string texto)
    {
        var normalizado = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(texto.Length);

        foreach (var caractere in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(caractere));
            }
        }

        return sb.ToString();
    }
}
