# NSI Docs - Documentacao Tecnica

## 1. Objetivo do Projeto
O **NSI Docs** e uma aplicacao web para consulta de documentacao tecnica em Markdown usando um pipeline multiagente sobre Semantic Kernel e um modelo acessado via Ollama.

A ideia central do projeto nao e apenas "fazer perguntas para um LLM". O sistema foi desenhado para separar a consulta em etapas menores e mais especializadas, de forma que cada agente resolva um problema especifico:

- entender a intencao da pergunta
- localizar os trechos mais relevantes da base documental
- condensar o contexto necessario
- produzir a resposta final ja pronta para exibicao

Essa divisao torna a aplicacao mais previsivel, mais facil de ajustar e menos dependente de um unico prompt gigante.

## 2. Problema que o Sistema Resolve
Quando a base de conhecimento cresce, um unico prompt com toda a documentacao deixa de ser viavel por varios motivos:

- custo de contexto
- latencia
- risco de resposta vaga
- dificuldade para controlar o foco da consulta
- baixa rastreabilidade sobre quais trechos influenciaram a resposta

O NSI Docs resolve isso transformando a pergunta do usuario em uma consulta estruturada. Em vez de mandar toda a documentacao para o modelo, ele:

1. interpreta a intencao da pergunta
2. recupera apenas as secoes mais provaveis de conter a resposta
3. reduz o ruido dessas secoes
4. so entao pede a resposta final

Na pratica, o sistema atua mais como um orquestrador de consulta tecnica do que como um chat simples.

## 3. Visao Geral da Arquitetura
Componentes principais:

- **Frontend/PWA**
  - Interface em arquivo unico (`wwwroot/index.html`)
  - Historico local, upload de `.md`, chat e renderizacao markdown
  - `manifest.webmanifest` e `service-worker.js` para experiencia PWA
- **Minimal API**
  - Exponibiliza as rotas de status, listagem, upload, recarga e consulta
  - Serve o frontend estatico
- **AplicacaoNsiDocs**
  - Fachada principal da aplicacao
  - Mantem os projetos carregados em memoria
  - Recria o orquestrador quando a base documental muda
- **OrquestradorConsulta**
  - Executa o pipeline multiagente de consulta
- **FabricaAgentes**
  - Cria os agentes dinamicamente
  - Registra e atualiza o plugin do Semantic Kernel
- **PluginDocumentacaoNsi**
  - Exponibiliza funcoes de indice e busca de secoes para o kernel
- **ParserSecoesMarkdown**
  - Converte arquivos `.md` em secoes tecnicas indexaveis
- **RecuperadorContexto**
  - Faz a selecao de secoes por heuristica de score
- **CarregadorDocumentacao**
  - Le os arquivos da pasta `documentacoes` e monta a base em memoria

## 4. Fluxo Fim a Fim
### 4.1 Entrada da Pergunta
O usuario envia uma pergunta para `POST /api/chat/perguntar`.

Opcionalmente, essa chamada pode carregar `documentosSelecionados`, que representa o subconjunto de `.md` que deve participar da consulta. Esse filtro e rigido: se ele vier preenchido, o restante da base e completamente ignorado naquela pergunta.

Em [Program.cs](C:\Users\fmota\Documents\projetos\Learning\NsiDocs\NsiDocs\Program.cs), a API:

- valida se a pergunta existe
- delega a consulta para `AplicacaoNsiDocs.PerguntarAsync`
- aceita `documentosSelecionados` como lista opcional de identificadores estaveis
- trata `timeout` como HTTP `408`
- retorna erro de negocio como `400`

Em [AplicacaoNsiDocs.cs](C:\Users\fmota\Documents\projetos\Learning\NsiDocs\NsiDocs\Servicos\AplicacaoNsiDocs.cs), a consulta passa por um `SemaphoreSlim`, o que significa que o estado interno da aplicacao e protegido durante:

- leitura da base carregada
- reprocessamento de documentos
- criacao do orquestrador da consulta atual

### 4.2 Criacao Dinamica dos Agentes
Antes de entrar em `ProcessarAsync`, a [AplicacaoNsiDocs.cs](C:\Users\fmota\Documents\projetos\Learning\NsiDocs\NsiDocs\Servicos\AplicacaoNsiDocs.cs) resolve quais documentos podem participar da consulta atual.

Esse passo usa o `identificador` de cada documento, que e o caminho relativo normalizado dentro de `documentacoes/`. Exemplos:

- `carreiras.md`
- `rh/integrador-rh.md`

Se a selecao vier vazia ou ausente, a consulta usa toda a base carregada. Se vier preenchida, a aplicacao filtra `_projetos` e cria uma nova [FabricaAgentes.cs](C:\Users\fmota\Documents\projetos\Learning\NsiDocs\NsiDocs\Agentes\FabricaAgentes.cs) apenas com esse subconjunto. Isso garante que:

- o `Planejador` veja um indice menor e mais focado
- o plugin do Semantic Kernel seja registrado somente com os documentos permitidos
- o fallback local use exatamente o mesmo universo

So depois disso o metodo `ProcessarAsync` em [OrquestradorConsulta.cs](C:\Users\fmota\Documents\projetos\Learning\NsiDocs\NsiDocs\Agentes\OrquestradorConsulta.cs) cria, por padrao, tres agentes por consulta:

- `Planejador`
- `AnalistaContexto`
- `RespondedorFinal`

Esses agentes sao construidos pela [FabricaAgentes.cs](C:\Users\fmota\Documents\projetos\Learning\NsiDocs\NsiDocs\Agentes\FabricaAgentes.cs) com prompts especializados e de responsabilidade unica.

Existe um override interno por variavel de ambiente (`NSIDOCS_MODO_ORQUESTRACAO`) para benchmark de arquiteturas com `1`, `2`, `3` ou `4` agentes, mas o default do projeto foi fixado em `3 agentes` porque entregou o melhor equilibrio entre latencia, qualidade e consistencia entre stream e resposta final.

O sistema nao usa um unico agente "faz tudo". Ele monta um pipeline explicito de handoff entre etapas.

### 4.3 Etapa 1: Planejamento da Consulta
O `Planejador` recebe a pergunta original e responde em um formato fechado:

```text
Projeto: [nome do projeto ou "todos"]
Temas: [lista curta separada por virgula]
Objetivo: [o que a resposta precisa entregar]
```

Esse formato e importante porque a orquestracao posterior depende de parsing deterministicamente simples, feito por `InterpretarPlanoConsulta`.

Exemplo de saida esperada:

```text
Projeto: carreiras
Temas: stack, backend, frontend, configuracoes
Objetivo: listar a stack tecnica do projeto de forma organizada
```

Esse planejamento resolve um problema importante: a pergunta do usuario pode ser vaga, mas a busca de contexto precisa ser mais estruturada. O `Planejador` transforma linguagem natural em intencao operacional.

### 4.4 Etapa 2: Recuperacao de Contexto
Antes da recuperacao em si, existe um corte importante de universo: o sistema so considera os documentos liberados pela selecao atual do usuario. Esse filtro impacta o pipeline inteiro, nao apenas a exibicao do frontend.

Depois que o plano e interpretado, o sistema tenta recuperar secoes relevantes em duas camadas:

1. caminho preferencial: plugin do Semantic Kernel
2. fallback local: `RecuperadorContexto`

#### Caminho preferencial: plugin
O metodo `BuscarSecoesRelevantesComPluginAsync` chama:

- `DocumentacaoPlugin.buscar_secoes_relevantes`

Esse plugin recebe:

- `pergunta`
- `projetoAlvo`
- `temasCsv`
- `quantidade`

Como o plugin e instanciado a partir da lista de projetos da consulta atual, ele nao precisa receber `documentosSelecionados` diretamente. O escopo ja chega filtrado na propria fabrica.

E devolve JSON com:

- `projeto`
- `titulo`
- `arquivo`
- `conteudo`

Mesmo quando a busca e feita pelo plugin, a selecao real ainda se apoia na logica interna do recuperador local, o que significa que o plugin serve como interface semanticamente organizada dentro do kernel, e nao como um motor de busca separado.

#### Fallback local
Se a chamada do plugin falhar por qualquer motivo, o sistema cai para:

- `RecuperadorContexto.RecuperarSecoes(...)`

Esse fallback evita que a consulta dependa exclusivamente da camada de plugin. Na pratica, ele aumenta robustez operacional: se houver falha de invocacao do kernel/plugin, a aplicacao ainda tenta responder usando a heuristica local.

### 4.5 Como o Score das Secoes e Calculado
O [RecuperadorContexto.cs](C:\Users\fmota\Documents\projetos\Learning\NsiDocs\NsiDocs\Servicos\RecuperadorContexto.cs) extrai termos da pergunta e dos temas planejados e calcula score por secao.

Desde a primeira otimização da camada de busca, a normalizacao dos campos nao acontece mais dentro de cada consulta. Durante a carga dos `.md`, o [CarregadorDocumentacao.cs](C:\Users\fmota\Documents\projetos\Learning\NsiDocs\NsiDocs\Servicos\CarregadorDocumentacao.cs) preenche em cada `SecaoDocumento`:

- `ProjetoNormalizado`
- `TituloNormalizado`
- `ConteudoNormalizado`
- `TokensTitulo`
- `TokensConteudo`
- `TokensCombinados`
- `OrdemNoProjeto`

Na consulta, o recuperador reutiliza esses campos ja preparados e so normaliza a pergunta e o projeto alvo.

Fatores relevantes do score:

- bonus forte para projeto alvo quando ele bate com a secao
- peso alto para termos da pergunta quando aparecem no titulo
- peso menor para termos da pergunta quando aparecem no conteudo
- peso intermediario para termos vindos de `Temas` e `Objetivo`
- bonus por cobertura da consulta:
  - quantos termos relevantes da pergunta aparecem na secao
  - quantos aparecem especificamente no titulo
- bonus por frases significativas:
  - bigramas/trigramas da pergunta e dos temas
  - isso ajuda em consultas como `fluxo de autenticacao`, `stack backend`, `camada de acesso`
- ponderacao por raridade do termo entre as secoes
  - termos raros valem mais do que termos muito repetidos
- bonus leve de vizinhanca estrutural
  - secoes adjacentes e da mesma trilha hierarquica podem subir um pouco se uma secao fortemente relacionada ja foi bem pontuada
- pequena penalizacao para secoes muito grandes

Outro detalhe importante: a analise lexical tambem gera variacoes simples de termos, como singular/plural e fragmentos de tokens compostos (`api-rest`, `automações`, `integrações`). Isso melhora a busca para documentacoes diferentes sem depender de regras especificas por projeto.

Isso mostra um ponto importante do projeto: a recuperacao nao e puramente vetorial nem totalmente delegada ao modelo. Existe uma camada heuristica explicita, mas agora mais generica, que ajuda a manter previsibilidade para qualquer conjunto novo de `.md`.

### 4.6 Corte entre Secoes Recuperadas e Secoes Utilizadas
Depois da busca, o sistema separa dois conceitos:

- `QuantidadeSecoesRecuperadas`
  - quantas secoes entram na fase de triagem inicial
- `QuantidadeSecoesUtilizadas`
  - quantas secoes vao realmente compor o contexto final enviado aos agentes seguintes

Hoje os defaults sao:

- recuperadas: `12`
- utilizadas: `6`

Esse corte serve para equilibrar recall e foco:

- recuperar demais pode trazer ruido
- usar poucas secoes demais pode perder contexto importante

### 4.7 Etapa 3: Consolidacao do Contexto
As secoes escolhidas passam por `MontarContexto`, que gera um bloco estruturado com:

- projeto
- secao
- arquivo de origem
- conteudo bruto da secao

Esse material vai para o `AnalistaContexto`, junto com:

- pergunta original
- projeto alvo
- temas
- objetivo

O `AnalistaContexto` nao responde o usuario. Ele faz uma reducao do contexto. Sua funcao e:

- eliminar repeticoes
- destacar pontos tecnicos importantes
- manter rastreabilidade por projeto e secao
- caber em um contexto menor para o `Respondedor`

Exemplo simplificado do tipo de entrada que vai para esse agente:

```text
Pergunta:
Que stack e usada no projeto do carreiras?

Plano de consulta:
Projeto: carreiras
Temas: stack, backend, frontend, configuracoes
Objetivo: listar a stack tecnica do projeto

Trechos recuperados:
## PROJETO: carreiras
### SECAO: Stack e Tecnologias > Back-end
(fonte: carreiras.md)
...
```

### 4.8 Etapa 4: Resposta Final
No pipeline padrao, o `RespondedorFinal` recebe:

- pergunta do usuario
- contexto consolidado pelo `AnalistaContexto`
- contrato do perfil da pergunta
- contrato do modo de resposta

Esse agente tem uma responsabilidade bem definida: responder de forma tecnica e objetiva, apenas com base no contexto consolidado, ja entregando markdown pronto para o chat.

Regras relevantes do prompt:

- responder em portugues BR
- nao inventar
- admitir ausencia de informacao quando necessario
- usar listas quando fizer sentido
- preservar a mesma base factual entre `curta`, `normal` e `detalhada`
- nao devolver payload cru, fragmentos soltos nem pseudo-tabelas quebradas

Aqui esta o ganho principal do fluxo multiagente padrao: o agente que gera a resposta final nao precisa decidir sozinho o que procurar em uma base grande. Ele ja recebe um contexto filtrado e um contrato de saida mais fechado.

### 4.9 Observacao sobre o Formatador
O projeto ainda mantem uma variante alternativa com `4 agentes`, em que a resposta tecnica passa por um `Formatador` separado.

Essa variante continua disponivel apenas para benchmark interno, mas nao e mais o default porque:

- adiciona mais latencia
- aumenta a diferenca entre preview do stream e resposta final
- cria mais um ponto de variacao no pipeline

Na arquitetura atual, a responsabilidade de resposta e formatacao final fica concentrada no `RespondedorFinal`.

### 4.10 Saida para a API e Frontend
Ao final do pipeline, o sistema devolve:

- `RespostaFinal`
- lista de `SecoesUtilizadas`

Esses dados sao convertidos pela API em:

```json
{
  "resposta": "markdown",
  "secoesUtilizadas": [
    { "projeto": "carreiras", "titulo": "Stack e Tecnologias > Back-end" }
  ]
}
```

No frontend:

- a resposta e renderizada como markdown
- as secoes utilizadas aparecem como chips de contexto
- o historico local salva pergunta e resumo

## 5. Papel de Cada Agente
### 5.1 Planejador
**Entrada**

- pergunta original do usuario
- indice resumido dos projetos e secoes

**Responsabilidade**

- identificar o projeto alvo
- resumir os temas tecnicos
- explicitar o objetivo da resposta

**Saida**

- `Projeto`
- `Temas`
- `Objetivo`

**Risco que reduz**

- recuperacao de contexto desalinhada com a intencao da pergunta

### 5.2 AnalistaContexto
**Entrada**

- pergunta original
- plano de consulta
- secoes recuperadas

**Responsabilidade**

- condensar o contexto realmente util
- remover repeticao
- destacar sinais tecnicos fortes

**Saida**

- resumo tecnico consolidado em ate 15 linhas

**Risco que reduz**

- excesso de ruido no contexto enviado ao respondedor

### 5.3 RespondedorFinal
**Entrada**

- pergunta do usuario
- contexto consolidado
- contrato do perfil da pergunta
- contrato do modo de resposta

**Responsabilidade**

- produzir a resposta final em markdown, ja pronta para o frontend

**Saida**

- resposta objetiva em markdown simples

**Risco que reduz**

- respostas vagas, inventadas ou distantes da documentacao recuperada

### 5.4 Formatador
**Entrada**

- resposta tecnica intermediaria do `Respondedor`

**Responsabilidade**

- melhorar legibilidade
- reduzir redundancia
- adequar a exibicao para chat/console

**Saida**

- resposta final pronta para frontend

**Risco que reduz**

- saidas confusas, longas demais ou com markdown ruim

**Observacao**

- esse agente nao participa do pipeline padrao atual; ele fica reservado para a variante de `4 agentes`, usada apenas em benchmark interno

## 6. Por que Multiplos Agentes?
### 6.1 Abordagem Atual
Na abordagem atual, cada agente faz uma parte do trabalho:

- um entende a intencao
- outro resume o contexto
- outro responde e ja entrega a saida final

### 6.2 Como seria com um Agente Unico
Com um agente unico, o prompt precisaria fazer tudo ao mesmo tempo:

- interpretar a pergunta
- escolher o que e relevante
- ignorar o que nao importa
- montar a resposta
- formatar a saida

Isso tende a gerar alguns efeitos colaterais:

- mais variacao entre respostas
- mais dificuldade para depurar
- maior chance de contexto mal aproveitado
- maior dependencia de um prompt unico dificil de manter

### 6.3 Ganhos da Abordagem Multiagente
- **Separacao de responsabilidade**
  - cada prompt fica menor e mais focado
- **Previsibilidade**
  - o plano do `Planejador` e parseado em formato fixo
- **Qualidade de recuperacao**
  - a busca usa projeto, temas e objetivo em vez de apenas a pergunta bruta
- **Facilidade de evolucao**
  - e possivel melhorar um agente sem redesenhar todos os outros
- **Depuracao**
  - fica mais facil descobrir se o problema esta no plano, na recuperacao, na consolidacao ou na resposta

Em resumo: o custo de ter varios agentes e maior complexidade de orquestracao, mas o retorno e maior controle sobre qualidade e manutencao.

## 7. Plugin do Semantic Kernel
O plugin registrado na fabrica se chama:

- `DocumentacaoPlugin`

Ele e recriado sempre que uma nova [FabricaAgentes.cs](C:\Users\fmota\Documents\projetos\Learning\NsiDocs\NsiDocs\Agentes\FabricaAgentes.cs) e instanciada. Na pratica, isso acontece:

- na inicializacao da aplicacao
- depois de um upload ou recarga do indice
- em cada consulta, quando a aplicacao monta a fabrica com o subconjunto de documentos selecionados

Isso e importante porque o plugin depende da lista de projetos visiveis naquela consulta especifica.

### 7.1 Como ele e registrado
Na [FabricaAgentes.cs](C:\Users\fmota\Documents\projetos\Learning\NsiDocs\NsiDocs\Agentes\FabricaAgentes.cs), o metodo `RegistrarPluginDocumentacao()`:

- remove uma versao anterior do plugin, se existir
- cria um plugin novo a partir de `PluginDocumentacaoNsi`
- adiciona o plugin ao kernel atual

Esse desenho evita plugin stale quando documentos novos sao enviados ou o indice e recarregado.
Tambem evita um problema de escopo: se o usuario marcar apenas alguns documentos no frontend, o plugin daquela consulta passa a enxergar somente esse subconjunto.

### 7.2 Por que o plugin existe
Ele existe para dar ao kernel uma interface explicita de acesso a documentacao, em vez de depender apenas de texto colado no prompt.

Na pratica, isso ajuda em dois pontos:

- organiza a busca como funcao do dominio
- deixa o fluxo preparado para evoluir a camada de recuperacao sem mudar a ideia geral do pipeline

### 7.3 Funcoes do plugin
#### `obter_indice_documentacao`
Uso:

- apresentar ao `Planejador` um panorama resumido dos projetos e secoes

Retorno:

- texto em formato de indice

#### `buscar_secoes_relevantes`
Uso:

- recuperar secoes tecnicas para uma pergunta

Parametros:

- `pergunta`
- `projetoAlvo`
- `temasCsv`
- `quantidade`

Retorno:

- JSON com projeto, titulo, arquivo e conteudo

### 7.4 Diferenca pratica entre as funcoes
- `obter_indice_documentacao`
  - serve para orientacao e planejamento
  - nao devolve conteudo completo
- `buscar_secoes_relevantes`
  - serve para montar o contexto efetivo da resposta
  - devolve conteudo de secoes selecionadas

## 8. Base Documental e Parsing
### 8.1 Fonte documental
A base do sistema vem da pasta:

- `documentacoes/`

Cada arquivo `.md` vira um `ProjetoDocumentacao`.

Durante a carga, cada documento recebe:

- `Nome`
- `Arquivo`
- `Identificador`

O `Identificador` e o caminho relativo normalizado dentro de `documentacoes/`, usado para filtro rigido no backend e persistencia de selecao no frontend.

### 8.2 Regra de parsing
O [ParserSecoesMarkdown.cs](C:\Users\fmota\Documents\projetos\Learning\NsiDocs\NsiDocs\Servicos\ParserSecoesMarkdown.cs) considera como secoes indexaveis cabecalhos de:

- `##`
- `###`
- `####`
- `#####`
- `######`

O titulo final da secao e montado como trilha hierarquica, por exemplo:

```text
Stack e Tecnologias > Back-end
```

Isso permite recuperar informacao em nivel mais fino do que o arquivo inteiro.

### 8.3 Consequencias praticas
- texto solto antes de um `##` nao vira secao recuperavel
- documentos mais bem estruturados geram resultados melhores
- a qualidade da resposta depende bastante da qualidade da organizacao dos `.md`

## 9. AplicacaoNsiDocs como Fachada Operacional
O [AplicacaoNsiDocs.cs](C:\Users\fmota\Documents\projetos\Learning\NsiDocs\NsiDocs\Servicos\AplicacaoNsiDocs.cs) centraliza o estado principal da aplicacao.

Responsabilidades:

- carregar e recarregar a base documental
- manter `_projetos` em memoria
- filtrar `_projetos` por `documentosSelecionados` quando a consulta pede um subconjunto
- criar `FabricaAgentes` e `OrquestradorConsulta` para a consulta atual
- serializar acesso com `SemaphoreSlim`
- tratar upload de novos documentos

Esse ponto e importante para manutencao: o orquestrador nao e um singleton fixo de logica imutavel. Ele depende do estado atual da base documental carregada e, agora, tambem do subconjunto de documentos permitido pela interface.

## 10. Configuracao e Tuning
### 10.1 Variaveis de ambiente
- `OLLAMA_MODEL`
  - modelo usado pelo kernel
  - default atual: `gpt-oss:120b-cloud`
- `OLLAMA_ENDPOINT`
  - endpoint HTTP do Ollama
  - default atual: `http://<ipv4-da-maquina>:11434`

Esses dois valores tambem podem ser alterados pela interface, na caixa `ollama`, com teste de conexao e persistencia em `configuracao.local.json`.

Para tuning avancado do pipeline, a aplicacao tambem aceita overrides por variavel de ambiente:

- `NSIDOCS_TIMEOUT_HTTP_SEGUNDOS`
- `NSIDOCS_TIMEOUT_PLANEJADOR_SEGUNDOS`
- `NSIDOCS_TIMEOUT_ANALISTA_SEGUNDOS`
- `NSIDOCS_TIMEOUT_RESPONDEDOR_SEGUNDOS`
- `NSIDOCS_TIMEOUT_FORMATADOR_SEGUNDOS`
- `NSIDOCS_SECOES_RECUPERADAS`
- `NSIDOCS_SECOES_UTILIZADAS`
- `NSIDOCS_LIMITE_LINHAS_ANALISTA`
- `NSIDOCS_LIMITE_LINHAS_FORMATADOR`
- `NSIDOCS_MODO_ORQUESTRACAO`

Esses overrides foram adicionados para benchmark e tuning operacional. O frontend continua configurando apenas `endpoint` e `modelo`.

### 10.2 Timeouts
- `TimeoutHttp`
  - timeout geral do `HttpClient`
  - relevante quando o modelo demora a responder
- `TimeoutPlanejador`
  - afeta a etapa de interpretacao da pergunta
- `TimeoutAnalista`
  - afeta consolidacao do contexto
- `TimeoutRespondedor`
  - normalmente o mais sensivel para respostas grandes
- `TimeoutFormatador`
  - so afeta a variante opcional de `4 agentes`

Defaults atuais do perfil adotado:

- `TimeoutPlanejador`: `60s`
- `TimeoutAnalista`: `60s`
- `TimeoutRespondedor`: `90s`
- `TimeoutFormatador`: `45s`

Quando ajustar:

- aumentar `TimeoutRespondedor` se a resposta estiver boa, mas estiver morrendo antes de terminar
- aumentar `TimeoutPlanejador` ou `TimeoutAnalista` se perguntas muito abertas estiverem falhando cedo
- revisar o modelo antes de simplesmente aumentar todos os timeouts

### 10.3 Quantidade de secoes
- `QuantidadeSecoesRecuperadas`
  - define o universo inicial de secoes candidatas
- `QuantidadeSecoesUtilizadas`
  - define quantas entram de fato na consolidacao

Defaults atuais do perfil adotado:

- `QuantidadeSecoesRecuperadas`: `12`
- `QuantidadeSecoesUtilizadas`: `6`

Quando ajustar:

- aumentar `Recuperadas` se o sistema estiver perdendo secoes importantes
- diminuir `Utilizadas` se as respostas estiverem difusas
- ajustar os dois em conjunto para equilibrar foco e cobertura

Antes de mexer nesses valores, existe um tuning mais barato e normalmente melhor: reduzir o escopo com `documentosSelecionados`. Em muitas perguntas internas, escolher 1 ou 2 documentos melhora mais do que aumentar o numero de secoes.

### 10.4 Limites de saida dos agentes
- `LimiteLinhasAnalista`
  - controla o quanto o `AnalistaContexto` pode condensar antes de enviar para o `Respondedor`
- `LimiteLinhasFormatador`
  - controla o teto de apresentacao da resposta final na arquitetura com `4 agentes`; no pipeline padrao, o mesmo contrato de formatacao ainda e reaproveitado pelo `RespondedorFinal`

Defaults atuais do perfil adotado:

- `LimiteLinhasAnalista`: `25`
- `LimiteLinhasFormatador`: `40`

Esses limites existem para evitar explosao de contexto e respostas prolixas demais, mas tambem influenciam diretamente a profundidade percebida no chat.

### 10.5 Perfil adotado apos benchmark
O projeto passou por benchmark comparando tres perfis:

- **Perfil A**: `8/4`, `45s/45s/60s/30s`, analista `15 linhas`, formatador `25 linhas`
- **Perfil B**: `12/6`, `60s/60s/90s/45s`, analista `25 linhas`, formatador `40 linhas`
- **Perfil C**: `16/8`, `75s/75s/120s/60s`, analista `35 linhas`, formatador `50 linhas`

Benchmark executado com `gpt-oss:120b-cloud` em `http://192.168.0.3:11434`.

Resultado consolidado:

- **Perfil A**
  - latencia media aproximada: `20,1s`
  - mais rapido em alguns cenarios, mas com respostas mais curtas e maior risco de superficialidade
- **Perfil B**
  - latencia media aproximada: `20,5s`
  - melhor equilibrio entre profundidade, organizacao e estabilidade
- **Perfil C**
  - latencia media aproximada: `27,1s`
  - aumento de custo e lentidao sem ganho consistente de qualidade; em algumas perguntas o resultado piorou

Por isso o **Perfil B** foi mantido como default do projeto.

### 10.6 Arquitetura multiagente adotada
Tambem foi executado um benchmark comparando arquiteturas com `4`, `3`, `2` e `1` agente.

Resultado consolidado:

- `4 agentes`
  - melhor modularidade, mas maior latencia media e maior diferenca entre preview do stream e resposta final
- `3 agentes`
  - melhor equilibrio entre qualidade, latencia e consistencia visual
- `2 agentes`
  - mais rapido, mas com pior desempenho em perguntas comparativas e multi-documento
- `1 agente`
  - latencia menor, porem com mais simplificacao e menor robustez em cenarios complexos

Por isso o projeto foi fixado com **`3 agentes` como default**:

- `Planejador`
- `AnalistaContexto`
- `RespondedorFinal`

## 11. Decisoes Tecnicas e Tradeoffs
### 11.1 Markdown como base documental
**Vantagens**

- facil de editar
- versionavel
- simples para upload e manutencao

**Tradeoff**

- depende de boa estruturacao por secoes

### 11.2 Parsing por secoes `##` a `######`
**Vantagens**

- simplicidade
- leitura hierarquica
- recuperacao mais granular

**Tradeoff**

- texto fora de cabecalhos fica invisivel para a recuperacao

### 11.3 Heuristica local de score
**Vantagens**

- previsibilidade
- baixo acoplamento com embeddings ou busca vetorial
- facilidade de explicar por que uma secao foi selecionada
- reaproveitamento de campos normalizados precomputados em memoria

**Tradeoff**

- heuristica precisa evoluir conforme surgem novos padroes de consulta

### 11.4 Preprocessamento em memoria
**Vantagens**

- reduz trabalho repetitivo por pergunta
- melhora latencia sem mudar o contrato da aplicacao
- mantem a busca totalmente local e simples de depurar

**Tradeoff**

- aumenta um pouco o custo da etapa de carga/recarrega
- adiciona mais dados mantidos em memoria por secao

### 11.5 Filtro rigido por documentos
**Vantagens**

- reduz ruido sem alterar a heuristica
- melhora tempo de resposta quando o usuario conhece o dominio da pergunta
- garante que o `Planejador`, o plugin e o fallback trabalhem sobre o mesmo recorte

**Tradeoff**

- se o usuario selecionar pouco demais, pode esconder a resposta certa
- exige reconciliacao da selecao quando a lista de documentos muda

### 11.6 Plugin com fallback
**Vantagens**

- integra bem com o kernel
- mantem robustez quando a camada de plugin falha

**Tradeoff**

- adiciona uma camada extra de orquestracao e pontos de observacao

### 11.7 Pipeline multiagente
**Vantagens**

- qualidade melhor controlada
- prompts menores
- depuracao por etapa

**Tradeoff**

- mais chamadas ao modelo
- mais latencia que um fluxo minimalista

## 12. Contratos da API
### `GET /api/status`
Retorna:

- `modelo`
- `quantidadeProjetos`
- `quantidadeSecoes`

### `GET /api/documentos`
Lista os documentos carregados.

Cada item retorna:

- `identificador`
  - caminho relativo normalizado dentro de `documentacoes/`
- `nome`
- `arquivo`
- `quantidadeSecoes`

### `POST /api/documentos/upload`
Recebe `multipart/form-data` com o campo `arquivo`.

Valida:

- existencia do arquivo
- extensao `.md`

Ao final:

- salva o arquivo na pasta de documentacoes
- recarrega o indice

### `POST /api/documentos/recarregar`
Reprocessa a pasta `documentacoes` e reconstrui a base em memoria.

### `GET /api/ollama/configuracao`
Retorna a configuracao atual em uso para:

- `endpoint`
- `modelo`

### `POST /api/ollama/testar-conexao`
Body:

```json
{
  "endpoint": "http://192.168.0.3:11434",
  "modelo": "llama3.1:latest"
}
```

Valida se:

- o endpoint responde ao Ollama
- o modelo informado pode ser validado no endpoint

### `POST /api/ollama/conectar`
Recebe o mesmo payload de teste, valida a conexao e salva a configuracao atual para uso imediato e persistencia local.

### `POST /api/chat/perguntar`
Body:

```json
{
  "pergunta": "Sua pergunta aqui",
  "documentosSelecionados": ["carreiras.md", "rh/integrador-rh.md"]
}
```

Regras:

- se `documentosSelecionados` vier vazio ou ausente, a consulta usa toda a base
- se vier preenchido, a consulta fica limitada a esses identificadores
- se nenhum identificador valido restar depois do filtro, a API responde `400`

Resposta:

```json
{
  "resposta": "markdown",
  "secoesUtilizadas": [
    { "projeto": "carreiras", "titulo": "Stack e Tecnologias > Back-end" }
  ]
}
```

## 13. Frontend e PWA
O frontend e servido como arquivo unico em:

- `wwwroot/index.html`

Pontos relevantes:

- renderiza a resposta em markdown
- mostra as secoes utilizadas
- mantem historico local em `localStorage`
- mantem a selecao atual de documentos em `localStorage`
- permite marcar todos ou limpar a selecao no painel `docs`
- permite filtrar a lista de documentos por nome, arquivo ou identificador diretamente no painel `docs`
- envia upload de `.md`
- funciona como PWA com shell offline

Com filtro ativo no painel `docs`, as acoes `todos` e `limpar` passam a atuar sobre os documentos visiveis naquele recorte.

Arquivos relacionados:

- `wwwroot/manifest.webmanifest`
- `wwwroot/service-worker.js`

Regra operacional importante:

- sempre que houver mudanca relevante de frontend/PWA, incrementar `CACHE_VERSION`

## 14. Exemplos Praticos
### 14.1 Exemplo de pergunta
```text
Que stack e usada no projeto do carreiras?
```

### 14.2 Exemplo resumido do plano
```text
Projeto: carreiras
Temas: stack, backend, frontend, configuracoes
Objetivo: listar a stack usada no projeto de forma organizada
```

### 14.3 Exemplo de secoes recuperadas
```text
carreiras -> Stack e Tecnologias
carreiras -> Stack e Tecnologias > Back-end
carreiras -> Stack e Tecnologias > Front-end
carreiras -> Stack e Tecnologias > Configuracoes Gerais
```

### 14.4 Exemplo do que chega ao respondedor
```text
Pergunta do usuario:
Que stack e usada no projeto do carreiras?

Contexto consolidado:
- Projeto: carreiras
- Back-end em ASP.NET Web Forms com .NET Framework 4.8
- Front-end com Bootstrap 4.x e jQuery 3.5.1
- Uso de SQL Server 2017
- Integracoes com Active Directory e SMTP
```

## 15. Troubleshooting do Pipeline Multiagente
### 15.1 Resposta generica ou rasa
Possiveis causas:

- secoes pouco relevantes
- consolidacao agressiva demais
- pergunta muito aberta

Acoes:

- revisar qualidade dos cabecalhos no `.md`
- reduzir o universo marcando apenas os documentos do dominio certo
- aumentar `QuantidadeSecoesRecuperadas`
- formular pergunta com mais contexto tecnico

### 15.2 Timeout em perguntas grandes
Possiveis causas:

- modelo lento
- contexto muito grande
- timeout curto para a etapa critica

Acoes:

- limitar a pergunta a um subconjunto de documentos quando possivel
- reduzir escopo da pergunta
- revisar modelo configurado
- ajustar `TimeoutRespondedor`
- reduzir ruido nas secoes usadas

### 15.3 Plugin indisponivel
Sintoma:

- a consulta ainda pode funcionar, mas usando o fallback local

Impacto:

- a aplicacao nao necessariamente cai
- a qualidade pode continuar aceitavel, dependendo da pergunta

### 15.4 Fallback local sendo usado
Se o plugin falhar, o sistema usa `RecuperadorContexto` diretamente.

Isso e um comportamento esperado de resiliencia, nao necessariamente um erro fatal.

### 15.5 Pergunta sem secoes relevantes
Quando nenhuma secao atinge score suficiente, a resposta final e:

```text
Nao encontrei trechos relevantes na documentacao.
```

Isso normalmente indica:

- pergunta fora do escopo da base
- documentacao insuficiente
- cabecalhos pouco expressivos
- filtro de documentos restritivo demais

### 15.6 Resposta com markdown ou ASCII inesperado
Se o modelo gerar diagramas ASCII ou pseudo-tabelas, a camada final de formatacao e o frontend tentam preservar esse conteudo como bloco de codigo.

Se isso ainda sair ruim:

- revisar o prompt do `Formatador`
- revisar a heuristica de normalizacao do frontend
- preferir estruturas mais explicitas no texto fonte da documentacao

## 16. Como Executar
No diretorio raiz do repositorio:

```powershell
dotnet restore NsiDocs/NsiDocs.csproj
dotnet run --project NsiDocs/NsiDocs.csproj --urls http://localhost:5001
```

Abrir no navegador:

- `http://localhost:5001`

## 17. Observacoes Finais para Manutencao
Se for preciso evoluir o sistema, a ordem natural de analise costuma ser:

1. qualidade da documentacao `.md`
2. qualidade da recuperacao de secoes
3. qualidade do plano gerado pelo `Planejador`
4. consolidacao feita pelo `AnalistaContexto`
5. resposta do `Respondedor`
6. limpeza final do `Formatador`

Esse ponto resume a principal vantagem do projeto: como o pipeline esta particionado, fica mais facil descobrir onde a qualidade esta se perdendo.
