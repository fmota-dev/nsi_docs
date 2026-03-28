Cada casa possui:
- ✅ Usuários exclusivos
- ✅ Unidades exclusivas
- ✅ Eleições exclusivas
- ✅ Dados estatísticos segregados
- ✅ Configuração de Active Directory própria (quando necessário)

### 2.2. Tabelas do Banco de Dados

#### 2.2.1. TB_CasasCAS

Tabela central que define as casas/organizações do sistema.

| Coluna | Tipo | Descrição |
|--------|------|-----------|
| PK_COD_CasaCAS | INT | Identificador único da casa |
| TXT_DescricaoCAS | VARCHAR | Nome da casa (ex: "SENAC", "SESC") |
| DAT_CadastroCAS | DATETIME | Data de cadastro |
| DAT_AlteracaoCAS | DATETIME | Data da última alteração |
| FK_COD_StatusCAS | INT | Status ativo/inativo |

**Valores Atuais:**
- `1` = SENAC
- `2` = SESC

#### 2.2.2. TB_InformacoesActiveDirectoryIAD

Armazena configurações de Active Directory customizadas por casa.

| Coluna | Tipo | Descrição |
|--------|------|-----------|
| PK_COD_InformacaoIAD | INT | Identificador único |
| TXT_ServidorIAD | VARCHAR | Endereço do servidor AD (ex: "dc.sesc.com.br") |
| TXT_UsuarioIAD | VARCHAR | Usuário de bind do AD |
| TXT_SenhaIAD | VARCHAR | Senha criptografada (AES Rijndael) |
| FK_COD_CasaIAD | INT | Casa à qual pertence a configuração |
| FK_COD_StatusIAD | INT | Status ativo/inativo |

**Observação Importante:**
- **SENAC**: Não possui registro nesta tabela. Usa o Active Directory padrão do domínio Windows.
- **SESC**: Possui registro (ID=1) com configuração customizada de AD externo.

#### 2.2.3. Colunas FK_COD_Casa Adicionadas

As seguintes tabelas receberam a coluna `FK_COD_Casa` para segregação:

| Tabela | Nova Coluna | Descrição |
|--------|-------------|-----------|
| TB_UsuariosUSU | FK_COD_CasaUSU | Define a qual casa o usuário pertence |
| TB_UnidadesUNI | FK_COD_CasaUNI | Define a qual casa a unidade pertence |
| TB_EleicaoELE | FK_COD_CasaELE | Define a qual casa a eleição pertence |

### 2.3. Fluxo de Login Multi-Casa

#### Tela de Login (default.aspx)

**Interface:**
1. Campo de matrícula
2. Campo de senha
3. **Dropdown de seleção de casa** (novo)
   - Opção 1: SENAC
   - Opção 2: SESC

**Validação JavaScript:**
```javascript
// Validação obrigatória - usuário deve selecionar uma casa antes de logar
if (ddlCasa.value === "0") {
    alert("Selecione a casa!");
    return false;
}
```

#### Autenticação por Casa

##### SENAC (Casa 1)

1. **Entrada do Usuário:**
   - Matrícula: Ex: `123456`
   - Senha: Senha do AD corporativo
   - Casa: **SENAC**

2. **Processo de Autenticação:**
   ```csharp
   // Usa AD padrão do domínio Windows
   ActiveDirectory.LoginUsuarioAD(matricula, senha)
   ```

3. **Características:**
   - ✅ Autentica diretamente com `sAMAccountName` (matrícula)
   - ✅ Usa `PrincipalContext` com AD padrão
   - ✅ Não requer configuração no banco

##### SESC (Casa 2)

1. **Entrada do Usuário:**
   - Matrícula: Ex: `7891011` (armazenada no campo `ipPhone` do AD)
   - Senha: Senha do AD do SESC
   - Casa: **SESC**

2. **Processo de Autenticação (2 Fases):**

   **Fase 1 - Busca por ipPhone:**
   ```csharp
   // Recupera configuração do AD do banco
   MInformacaoAD infoAD = InformacoesAD.ConsultaInformacoesAdPorCasa(2);
   
   // Descriptografa senha
   string senhaAD = Criptografias.Decrypt(infoAD.Txt_Senha);
   
   // Busca sAMAccountName pelo ipPhone
   string sAMAccountName = BuscarSAMAccountNamePorIpPhone(
       matricula,           // O usuário digita o ipPhone
       infoAD.Txt_Servidor, // Ex: "dc.sesc.com.br"
       infoAD.Txt_Usuario,  // Ex: "admin@sesc.com.br"
       senhaAD             // Senha descriptografada
   );
   ```

   **Query LDAP Executada:**
   ```ldap
   (ipPhone=7891011)
   ```

   **Fase 2 - Autenticação:**
   ```csharp
   // Autentica com o sAMAccountName encontrado
   PrincipalContext.ValidateCredentials(sAMAccountName, senhaUsuario);
   ```

3. **Características:**
   - ✅ Autenticação em **2 etapas** (busca + validação)
   - ✅ Campo `ipPhone` do AD armazena a matrícula do SESC
   - ✅ Usa AD externo configurado no banco
   - ✅ Senha do bind é descriptografada antes do uso

#### Validação de Casa do Usuário

Após autenticação no AD, o sistema valida se o usuário pertence à casa selecionada:

```csharp
MUsuario usuarioLogado = Usuario.ConsultaUsuarioPorMatricula(matricula);

if (usuarioLogado.Fk_Cod_Casa != codCasaSelecionada)
{
    // ERRO: Usuário não pertence à casa selecionada
    ScriptManager.RegisterStartupScript(
        this, 
        GetType(), 
        "toastAviso", 
        "exibirAlerta('Usuário não pertence à casa selecionada!','warning');", 
        true
    );
    Session.Abandon();
    return;
}
```

**Segurança:** Impede que um usuário autenticado no AD de uma casa acesse dados de outra casa.

#### Session State

Após login bem-sucedido, as seguintes informações são armazenadas na sessão:

```csharp
Session["CodCasa"] = codCasa;              // 1=SENAC, 2=SESC
Session["Usuario"] = nomeExibicao;         // Nome do AD
Session["Matricula"] = matricula;          // Matrícula/ipPhone
Session["CodUsuario"] = usuario.Pk_Cod_Usuario;
Session["CodUnidade"] = usuario.Fk_Cod_Unidade;
Session["UnidadeUsuario"] = usuario.Unidade.Txt_Nome;
Session["Perfil"] = usuario.Perfil.Pk_Cod_Perfil;
```

**`Session["CodCasa"]`** é a chave fundamental para toda segregação de dados no sistema.

---

## 3. Acesso e Permissões

O sistema possui dois níveis de acesso principais, definidos no banco de dados:
- **Perfil 1**: Administrador
- **Perfil 2**: Usuário Padrão (Eleitor)

### 3.1. Autenticação

1.  **Login via Active Directory (AD)**: A autenticação inicial é feita validando as credenciais do usuário (matrícula e senha) contra o Active Directory correspondente à casa selecionada.
2.  **Validação no Banco de Dados**: Após a validação no AD, o sistema verifica se a matrícula do usuário existe e está ativa no banco de dados local (`TB_UsuariosUSU`).
3.  **Validação de Casa**: Garante que o usuário autenticado pertence à casa selecionada no login.
4.  **Sessão de Usuário**: Se todas as validações forem bem-sucedidas, os dados do usuário (incluindo `CodCasa`) são armazenados na `Session`.

### 3.2. Regras de Acesso por Período

O acesso ao sistema para usuários não-administradores é restrito aos seguintes períodos, com base na **última eleição cadastrada para sua unidade**:

-   Durante o **período de inscrição** de candidatos.
-   Durante o **período de votação**.
-   No intervalo **entre o fim das inscrições e o início da votação**.
-   Até **15 dias após o término** da votação.

Fora desses períodos, o usuário recebe uma mensagem informando que o sistema está indisponível.

### 3.3. Níveis de Permissão

-   **Administrador (Perfil 1)**:
    -   Acesso irrestrito ao sistema, a qualquer momento.
    -   ✅ **Visualiza apenas dados de sua casa** (`Session["CodCasa"]`)
    -   Pode criar, visualizar e editar eleições de sua casa.
    -   Pode visualizar apuração de unidades de sua casa.
    -   Pode aprovar ou reprovar candidaturas de sua casa.
    -   Pode visualizar lista de candidatos de eleições de sua casa.
    -   Tem acesso a dashboard com estatísticas de sua casa.
    -   **Também pode se inscrever e votar**: Se a sua unidade estiver em um período de eleição ativo.

-   **Usuário Padrão (Perfil 2 - Eleitor)**:
    -   Acesso restrito aos períodos definidos.
    -   ✅ **Visualiza apenas dados de sua casa**
    -   Pode se inscrever como candidato durante o período de inscrição.
    -   Pode votar (uma única vez) durante o período de votação.
    -   Pode visualizar a apuração de sua unidade apenas **após o término da eleição** e por até 15 dias.
    -   Visualiza um menu simplificado, com acesso apenas às funcionalidades permitidas para o período.

### 3.4. Segregação de Dados por Casa

**Todas as consultas SQL incluem filtro por casa:**

```sql
-- Exemplo: Consulta de Unidades
SELECT * FROM TB_UnidadesUNI 
WHERE FK_COD_StatusUNI = 1 
  AND FK_COD_CasaUNI = @CodCasa

-- Exemplo: Consulta de Eleições
SELECT * FROM TB_EleicaoELE 
WHERE FK_COD_CasaELE = @CodCasa

-- Exemplo: Contagem de Usuarios
SELECT COUNT(*) FROM TB_UsuariosUSU 
WHERE FK_COD_StatusUSU = 1 
  AND FK_COD_CasaUSU = @CodCasa
```

**Classes com Métodos Filtrados:**

| Classe | Método Original | Método Filtrado por Casa |
|--------|-----------------|--------------------------|
| `Unidades` | `ConsultaTodasUnidades()` | `ConsultaUnidadesPorCasa(int codCasa)` |
| `Eleicao` | `ConsultaTodasAsEleicoes()` | `ConsultaEleicoesPorCasa(int codCasa)` |
| `Usuarios` | `ContarTotalUsuariosAtivos()` | `ContarTotalUsuariosAtivosPorCasa(int codCasa)` |
| `Votos` | `ContarTotalVotos()` | `ContarTotalVotosPorCasa(int codCasa)` |

**Uso nos Controllers:**

```csharp
int codCasa = int.Parse(Session["CodCasa"].ToString());

// Sempre passa codCasa para métodos de consulta
var unidades = new Unidades().ConsultaUnidadesPorCasa(codCasa);
var eleicoes = new Eleicao().ConsultaEleicoesPorCasa(codCasa);
```

---

## 4. Ciclo de Vida da Eleição

### 4.1. Eleições (Cadastro e Gestão)

-   **Responsável**: Administrador.
-   **Página**: `eleicoes.aspx`.

Esta página centraliza a gestão de todas as eleições do sistema **da casa do administrador logado**.

#### Funcionalidades e Ações:

1.  **Listagem e Filtro**:
    -   A página exibe uma lista de eleições **apenas da casa do usuário** (`Session["CodCasa"]`).
    -   Mostra o nome do edital, a unidade, o status e os períodos de inscrição e votação.
    -   É possível filtrar a lista por ano usando um menu suspenso.

2.  **Botão "Cadastrar Eleição"**:
    -   Abre um modal que permite o cadastro de uma nova eleição.
    -   **Dropdown de Unidades:** Carrega apenas unidades da casa do administrador.
    -   **Persistência:** Ao salvar, o `FK_COD_CasaELE` é automaticamente preenchido com `Session["CodCasa"]`.
    
    **Regras de Cadastro**:
        -   É necessário definir um nome de edital, uma unidade, um período de inscrição e um período de votação.
        -   O **período de inscrição deve terminar antes do início do período de votação**.
        -   As datas de início devem ser anteriores às datas de fim correspondentes.
        -   É possível anexar um arquivo de edital (`.pdf`, `.doc`, `.docx`) de até 10MB.

3.  **Ações por Eleição**:
    -   **Editar**: Redireciona para `editar-eleicao.aspx`, onde é possível alterar informações da eleição. O dropdown de unidades também é filtrado por casa.
    -   **Candidatos**: Modal com lista de candidatos inscritos (apenas da eleição/casa correspondente). O administrador pode aprovar/reprovar candidaturas.
    -   **Resultado**: Visível após término da eleição. Exibe resultado final da apuração.

### 4.2. Inscrição de Candidatos

-   **Responsável**: Usuário Padrão (Eleitor).
-   **Página**: `inscricao.aspx`.

#### Regras de Negócio:

1.  **Período de Inscrição**: A inscrição só é permitida dentro do período (`DAT_InicioInscricaoELE` e `DAT_FimInscricaoELE`) da última eleição da unidade do usuário **da mesma casa**.
2.  **Inscrição Única**: Um usuário só pode se inscrever **uma vez** por eleição.
3.  **Status da Inscrição**:
    -   Ao se inscrever, a candidatura fica com o status **"Pendente"**.
    -   O administrador **da mesma casa** pode alterar o status para **"Candidatura Aprovada"** ou **"Candidatura Não Aprovada"**.
4.  **Foto do Candidato**: O candidato pode (opcionalmente) enviar uma foto (`.jpg`, `.jpeg`, `.png`) de até 2MB no momento da inscrição ou atualizá-la posteriormente.
5.  **Comprovante**: Após a inscrição, o usuário pode visualizar e baixar um comprovante em PDF.
6.  **Aprovação**: Quando um administrador aprova uma candidatura, um registro correspondente é criado na tabela `TB_VotosVOT` com `NUM_QtdVotosVOT` inicializado em zero.

**Segregação:** O usuário só pode se inscrever em eleições de unidades da mesma casa à qual pertence.

### 4.3. Votação

-   **Responsável**: Usuário Padrão (Eleitor).
-   **Página**: `votacao.aspx`.

#### Regras de Negócio:

1.  **Período de Votação**: A votação só é permitida dentro do período (`DAT_InicioELE` e `DAT_FimELE`) da eleição ativa para a unidade do usuário **da mesma casa**.
2.  **Voto Único**: O sistema utiliza a tabela `TB_RegistroVotoREV` para garantir que cada usuário possa votar **apenas uma vez** por eleição.
    -   Antes de exibir os candidatos, o sistema verifica se o `CodUsuario` já possui um registro de voto para a `CodEleicao` ativa.
3.  **Confirmação do Voto**: O voto é computado em duas etapas:
    -   O usuário clica no candidato desejado, o que abre um modal de confirmação.
    -   Ao confirmar, o voto é incrementado na tabela `TB_VotosVOT` e um registro é criado na `TB_RegistroVotoREV`.
4.  **Interface Pós-Voto**: Após votar, a lista de candidatos é ocultada e uma mensagem de "Voto computado com sucesso" é exibida.
5.  **Candidatos Exibidos**: Apenas candidatos com status **"Candidatura Aprovada"** **da eleição da casa do usuário** são exibidos na tela de votação.

**Segregação:** Usuários só votam em candidatos de eleições de sua própria casa.

### 4.4. Apuração dos Resultados

A visualização dos resultados é dividida em duas funcionalidades distintas, dependendo do status da eleição. **Sempre filtrada por casa.**

#### 4.4.1. Apuração de Eleições em Andamento

-   **Responsável**: Administrador.
-   **Página**: `apuracao.aspx`.

1.  **Acesso do Administrador**: Administradores podem acessar esta página a qualquer momento para visualizar os **resultados parciais** de uma eleição que ainda está em andamento **de sua casa**.
2.  **Dropdown de Unidades:** Carrega apenas unidades com eleições ativas **da casa do administrador** (`ConsultaUnidadesComEleicaoAtivaPorCasa(codCasa)`).
3.  **Dados Exibidos**: A página exibe apenas dados estatísticos de participação, como:
    -   Gráficos de pizza com o progresso da votação.
    -   Cards com o total de votos, eleitores que já votaram e percentual de participação.
    -   **Importante**: A lista detalhada com o ranking de votos por candidato **não é exibida** para eleições em andamento, garantindo a integridade do processo.

#### 4.4.2. Resultado de Eleições Encerradas

-   **Responsável**: Administrador e Usuário Padrão.
-   **Origem**: Botão "Resultado" na página `eleicoes.aspx`.

1.  **Disponibilidade para Eleitores**: Usuários padrão só podem visualizar o resultado final **após a data e hora de término** da eleição (`DAT_FimELE`) e por até 15 dias **de eleições de sua casa**.
2.  **Acesso do Administrador**: O botão "Resultado" na tela `eleicoes.aspx` fica visível para o administrador assim que a eleição é encerrada, funcionando como um histórico **de eleições de sua casa**.
3.  **Dados Exibidos**: Ao clicar no botão, um modal exibe o **resultado final e completo**, incluindo:
    -   A lista de candidatos com a classificação final (ranking por votos).
    -   As estatísticas finais de participação (total de votos, eleitores, etc.).
4.  **Exportação**: Administradores podem exportar o relatório de apuração completo em formato PDF.

**Segregação:** Cada casa visualiza apenas resultados de suas próprias eleições.

---

## 5. Dashboard e Estatísticas (principal.aspx)

### 5.1. Dashboard Administrativo (Perfil 1)

O dashboard exibe estatísticas **filtradas por casa**:

```csharp
int codCasa = int.Parse(Session["CodCasa"].ToString());

var usuarios = new Usuarios();
litTotalUsuarios.Text = usuarios.ContarTotalUsuariosAtivosPorCasa(codCasa).ToString();

var unidades = new Unidades();
litTotalUnidades.Text = unidades.ContarTotalUnidadesAtivasPorCasa(codCasa).ToString();

var eleicao = new Eleicao();
litTotalEleicoes.Text = eleicao.ContarTotalEleicoesPorCasa(codCasa).ToString();

var votos = new Votos();
litTotalVotos.Text = votos.ContarTotalVotosPorCasa(codCasa).ToString();
```

**Cards Exibidos:**
- 📊 Total de Usuários Ativos (da casa)
- 🏢 Total de Unidades Ativas (da casa)
- 🗳️ Total de Eleições (da casa)
- ✅ Total de Votos Computados (da casa)

### 5.2. Dashboard do Eleitor (Perfil 2)

Exibe informações da eleição ativa ou próxima **da unidade e casa do usuário**:

- Status atual (período de inscrição, votação, apuração)
- Datas importantes
- Botão de acesso à apuração (quando disponível)

---

## 6. Classes e Responsabilidades Principais

### 6.1. Camada de Negócio

-   **`Eleicao.cs`**: Gerencia criação, consulta e atualização de eleições. Inclui métodos filtrados por casa.
-   **`CandidatosEleicoes.cs`**: Gerencia inscrição, aprovação/reprovação e consulta de candidatos.
-   **`Votos.cs`**: Controla lógica de registro de votos, verificação de voto único e cálculo dos resultados da apuração. Inclui contagem filtrada por casa.
-   **`Usuario.cs`** (Usuarios): Realiza consultas de usuários no banco de dados. Inclui contagem filtrada por casa.
-   **`Unidades.cs`**: Gerencia consultas de unidades. Inclui métodos filtrados por casa.
-   **`Perfis.cs`**: Verifica o perfil de permissão de um usuário.
-   **`InformacoesAD.cs`**: **[NOVA]** Consulta configurações de Active Directory por casa.
-   **`ActiveDirectory.cs`**: Valida credenciais de login no Active Directory. Suporta autenticação em AD padrão (SENAC) e customizado (SESC).
-   **`Arquivos.cs`** e **`Imagem.cs`**: Gerenciam upload, download e registro de arquivos (editais e fotos de candidatos).
-   **`Criptografias.cs`**: Criptografia/descriptografiação AES Rijndael para senhas do AD.
-   **`GerarLog.cs`**: Classe utilitária para registrar logs de erros em arquivos de texto usando Serilog.

### 6.2. Camada de Dados (Models)

-   **`MUsuario.cs`**: Propriedades: `Pk_Cod_Usuario`, `Txt_Matricula`, `Txt_Nome`, **`Fk_Cod_Casa`**, `Casa`, `Perfil`, `Unidade`
-   **`MEleicao.cs`**: Propriedades: `Pk_Cod_Eleicao`, `Dat_Inicio`, `Dat_Fim`, `Dat_InicioInscricao`, `Dat_FimInscricao`, **`Fk_Cod_Casa`**, `Casa`, `Unidade`, `Status`
-   **`MUnidade.cs`**: Propriedades: `Pk_Cod_Unidade`, `Txt_Nome`, **`Fk_Cod_Casa`**
-   **`MCasa.cs`**: **[NOVA]** Propriedades: `Pk_Cod_Casa`, `Txt_NomeCasa`, `Dat_Cadastro`, `Dat_Alteracao`, `Fk_Cod_Status`
-   **`MInformacaoAD.cs`**: **[NOVA]** Propriedades: `Pk_Cod_InformacoesAD`, `Txt_Servidor`, `Txt_Usuario`, `Txt_Senha`, `Fk_Cod_Casa`, `Fk_Cod_Status`
-   **`MCandidatosEleicoes.cs`**: Propriedades dos candidatos inscritos
-   **`MVoto.cs`**: Propriedades dos votos
-   **`MResultadoApuracao.cs`**: DTO com resultado da apuração

### 6.3. Camada de Acesso a Dados

-   **`SQLServer.cs`**: Abstração do ADO.NET para executar queries parametrizadas
-   **`Conexao.cs`**: Gerencia connection string do banco de dados
-   **`Funcoes.cs`**: Funções utilitárias de banco de dados

---

## 7. Segurança

### 7.1. Autenticação

- ✅ **Active Directory**: Validação contra servidores AD corporativos
- ✅ **Multi-Fase (SESC)**: Busca LDAP + Autenticação separadas
- ✅ **Validação de Casa**: Garante que usuário pertence à casa selecionada

### 7.2. Autorização

- ✅ **Perfis**: Controle de acesso por perfil (Admin/Eleitor)
- ✅ **Períodos**: Restrição temporal de acesso para eleitores
- ✅ **Segregação por Casa**: Todos os dados filtrados por `Session["CodCasa"]`

### 7.3. Proteção de Dados

- ✅ **SQL Injection**: Queries parametrizadas em todas as operações
- ✅ **Criptografia**: Senhas de AD armazenadas com AES Rijndael
- ✅ **Sessão**: Dados sensíveis apenas em Session server-side
- ✅ **Validação de Entrada**: `TryParse` e validações de tipo

### 7.4. Logs

- ✅ **Serilog**: Framework robusto de logging
- ✅ **GerarLog**: Wrapper para padronização
- ✅ **Rastreabilidade**: Logs incluem usuário e contexto do erro

---

## 8. Diagrama de Fluxo - Autenticação Multi-Casa

```
┌─────────────────────────────────────────────────────────────┐
│                     TELA DE LOGIN                            │
│  [Matrícula] [Senha] [Dropdown Casa: SENAC/SESC] [Entrar]  │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
                    ┌───────────────┐
                    │ Casa = SENAC? │
                    └───────────────┘
                      │           │
                 SIM  │           │  NÃO (SESC)
                      │           │
         ┌────────────┘           └────────────┐
         ▼                                     ▼
┌─────────────────────┐            ┌──────────────────────────┐
│ AD Padrão Windows   │            │ Consulta TB_Informacoes  │
│ PrincipalContext()  │            │ ActiveDirectoryIAD       │
│                     │            │ WHERE FK_COD_CasaIAD = 2 │
│ ValidateCredentials │            └──────────────────────────┘
│ (matricula, senha)  │                        │
└─────────────────────┘                        ▼
         │                          ┌──────────────────────────┐
         │                          │ Descriptografa Senha AD  │
         │                          │ Criptografias.Decrypt()  │
         │                          └──────────────────────────┘
         │                                     │
         │                                     ▼
         │                          ┌──────────────────────────┐
         │                          │ LDAP Search              │
         │                          │ Filter: (ipPhone=matricula)│
         │                          │ Return: sAMAccountName   │
         │                          └──────────────────────────┘
         │                                     │
         │                                     ▼
         │                          ┌──────────────────────────┐
         │                          │ PrincipalContext(AD SESC)│
         │                          │ ValidateCredentials      │
         │                          │ (sAMAccountName, senha)  │
         │                          └──────────────────────────┘
         │                                     │
         └─────────────┬───────────────────────┘
                       │
                       ▼
           ┌────────────────────────┐
           │ Consulta TB_UsuariosUSU│
           │ WHERE TXT_MatriculaUSU  │
           └────────────────────────┘
                       │
                       ▼
           ┌────────────────────────┐
           │ Valida Casa do Usuário │
           │ usuarioLogado.Fk_Cod_Casa│
           │ == codCasaSelecionada?  │
           └────────────────────────┘
                 │           │
            SIM  │           │  NÃO
                 │           │
                 ▼           ▼
        ┌─────────────┐  ┌──────────────┐
        │ Session[]   │  │ Erro: Casa   │
        │ CodCasa     │  │ inválida     │
        │ CodUsuario  │  │ Abandona     │
        │ Matricula   │  │ Session      │
        │ Perfil      │  └──────────────┘
        └─────────────┘
                 │
                 ▼
        ┌─────────────┐
        │ Redirect    │
        │ Dashboard   │
        └─────────────┘
```

---

## 9. Convenções e Padrões de Código

### 9.1. Nomenclatura

**Classes:**
- Models: Prefixo `M` + PascalCase (ex: `MUsuario`, `MCasa`)
- Business: PascalCase sem prefixo (ex: `Usuario`, `Eleicao`)
- Utilitários: PascalCase descritivo (ex: `GerarLog`, `ActiveDirectory`)

**Métodos:**
- Consulta: `Consulta` + Entidade + Filtro (ex: `ConsultaUsuarioPorMatricula()`)
- Inserção: `Adicionar` + Entidade (ex: `AdicionarEleicao()`)
- Atualização: `Atualizar` + Entidade (ex: `AtualizarEleicao()`)
- Contagem: `Contar` + Descrição (ex: `ContarTotalVotosPorCasa()`)

**Propriedades (Hungarian Notation):**
- `Pk_Cod_` = Primary Key
- `Fk_Cod_` = Foreign Key
- `Txt_` = Texto (string)
- `Dat_` = Data (DateTime)
- `Num_` = Numérico

### 9.2. Instanciação de Objetos

**Padrão Recomendado:** `var`
```csharp
var usuarios = new Usuarios();
var eleicao = new Eleicao();
```

### 9.3. Acesso a Dados

**Padrão:**
```csharp
public class NomeDaClasse
{
    private readonly SQLServer data_access = new SQLServer(Conexao.ConexaoSQL);
    
    public TipoRetorno Metodo()
    {
        data_access.ClearParameter();
        data_access.PrepareQuery("SELECT ...");
        data_access.AddParameter("@Param", valor);
        return data_access.GetDataTable();
    }
}
```

### 9.4. Tratamento de Exceções

**Em Pages (.aspx.cs):**
```csharp
try
{
    // Lógica
}
catch (Exception ex)
{
    GerarLog.RegistrarErro(ex, "Mensagem", Session["Usuario"]?.ToString() ?? "Desconhecido", "log-arquivo.txt");
    ScriptManager.RegisterStartupScript(this, GetType(), "toastErro", "exibirAlerta('Erro!','error');", true);
}
```

### 9.5. Validação de Session

```csharp
if (!int.TryParse(Session["CodUnidade"]?.ToString(), out int codUnidade) ||
    !int.TryParse(Session["CodUsuario"]?.ToString(), out int codUsuario) ||
    codUnidade == 0 || codUsuario == 0)
{
    Response.Redirect("~/", false);
    return;
}
```

---

## 10. Referências Técnicas

- **Connection String**: `Conexao.ConexaoSQL` (configurada em `Web.config`)
- **Logs**: `~/logs/` (diversos arquivos por contexto)
- **Uploads**: `~/uploads/` (fotos de candidatos e editais)
- **Framework de Logging**: Serilog
- **Criptografia**: AES Rijndael (256 bits)
- **LDAP/AD**: System.DirectoryServices, System.DirectoryServices.AccountManagement

---

**Última Atualização:** 2024  
**Versão:** 2.0 - Multi-Casa (SENAC/SESC)  
**Autores:** Equipe de Desenvolvimento SNC_Cipa
