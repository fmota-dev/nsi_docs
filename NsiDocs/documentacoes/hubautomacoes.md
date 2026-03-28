# 🔌 Documentação da API REST - SNC Hub Automações

Bem-vindo à documentação da API do SNC Hub Automações. Este documento detalha a arquitetura, o fluxo de autenticação e todos os endpoints disponíveis para interação com a plataforma.

**Versão da API:** 2.0.0

## 📋 Índice

1.  [Visão Geral e Arquitetura](#1-visão-geral-e-arquitetura)
2.  [Autenticação](#2-autenticação)
3.  [Endpoints da API](#3-endpoints-da-api)
    *   [Autenticação (AutenticacaoController)](#autenticação-autenticacaocontroller)
    *   [Gerenciamento de Automações (AutomacaoController)](#gerenciamento-de-automações-automacaocontroller)
    *   [Gerenciamento de Ambientes (AmbienteExecucaoController)](#gerenciamento-de-ambientes-ambienteexecucaocontroller)
    *   [Execução e Histórico (ExecucoesController)](#execução-e-histórico-execucoescontroller)
4.  [Modelos de Dados (DTOs)](#4-modelos-de-dados-dtos)
5.  [Status Codes e Respostas](#5-status-codes-e-respostas)

---

## 1. Visão Geral e Arquitetura

O SNC Hub Automações é uma plataforma para centralizar, executar e monitorar automações baseadas em scripts (atualmente Python). A API REST é o núcleo da plataforma, permitindo a comunicação entre o frontend (Blazor) e o backend.

### Tecnologias Utilizadas

| Tecnologia | Propósito |
| :--- | :--- |
| **.NET 8** | Framework principal da aplicação, garantindo performance e recursos modernos. |
| **ASP.NET Core** | Utilizado para construir a API RESTful, gerenciando rotas, controllers e requisições HTTP. |
| **Dapper & Microsoft.Data.SqlClient** | Utilizado para interagir diretamente com o SQL Server, executando queries e mapeando resultados. |
| **Hangfire** | Biblioteca para criação, execução e gerenciamento de tarefas em background (jobs). Essencial para a execução assíncrona das automações. |
| **Serilog** | Biblioteca de logging estruturado, configurada para registrar informações em console e arquivos. |
| **JWT (JSON Web Tokens)** | Padrão utilizado para a autenticação segura e stateless da API. |

### Arquitetura em Camadas

A API segue um padrão de arquitetura em camadas para separar responsabilidades, facilitar a manutenção e os testes.

```
┌───────────────────────────┐
│   SNC_HubAutomacoes.Web   │ (Frontend Blazor)
└─────────────┬─────────────┘
              │ (HTTP Requests)
┌─────────────▼─────────────┐
│  SNC_HubAutomacoes.Api    │ (Backend)
│ ┌───────────────────────┐ │
│ │     Controllers       │ │ ◄── Camada de Entrada (Endpoints)
│ └──────────┬────────────┘ │
│            │              │
│ ┌──────────▼────────────┐ │
│ │       Services        │ │ ◄── Lógica de Negócio
│ └──────────┬────────────┘ │
│            │              │
│ ┌──────────▼────────────┐ │
│ │     Repositories      │ │ ◄── Acesso a Dados (SQL)
│ └──────────┬────────────┘ │
│            │              │
└────────────▼──────────────┘
      ┌────────────────┐
      │ Banco de Dados │ (SQL Server)
      └────────────────┘
```

1.  **Controllers (`/Controllers`)**
    *   **Responsabilidade:** É a camada de entrada da API. Recebe as requisições HTTP, valida os dados de entrada (DTOs) e orquestra a chamada para os serviços apropriados. Não contém lógica de negócio.
    *   **Exemplo:** `AutomacaoController.cs` recebe um `POST /api/automacao/executar` e chama o `BackgroundJobService` para enfileirar a execução.

2.  **Services (`/Services`)**
    *   **Responsabilidade:** Contém a lógica de negócio da aplicação. Processa os dados recebidos dos controllers, aplica regras de negócio e coordena as operações entre diferentes repositórios.
    *   **Exemplo:** `AutomacaoService.cs` valida se um identificador de automação já existe antes de chamar o repositório para criar um novo registro.

3.  **Repositories (`/Repositories`)**
    *   **Responsabilidade:** Camada de acesso a dados. Isola a lógica de comunicação com o banco de dados (queries SQL com Dapper). É a única camada que interage diretamente com o banco.
    *   **Exemplo:** `AutomacaoRepository.cs` contém os métodos com as queries SQL para `SELECT`, `INSERT`, `UPDATE` e `DELETE` nas tabelas de automações e parâmetros.

4.  **Shared Project (`SNC_HubAutomacoes.Shared`)**
    *   **Responsabilidade:** Contém os DTOs (Data Transfer Objects) e modelos de requisição/resposta que são compartilhados entre o projeto da API e o projeto Web. Isso garante consistência e evita duplicação de código.

---

## 2. Autenticação

A API é protegida usando **JSON Web Tokens (JWT)**. Endpoints que exigem autenticação são decorados com o atributo `[Authorize]`. Para acessá-los, o cliente deve primeiro se autenticar e depois enviar o token recebido em todas as requisições subsequentes.

### Fluxo de Autenticação

O processo utiliza o banco de dados da própria aplicação como fonte de verdade.

**Etapa 1: Obtenção do Token**

1.  O cliente (frontend) envia as credenciais do usuário (`NomeUsuario` e `Senha`) via `POST` para o endpoint `api/autenticacao/autenticar`.
2.  O `AutenticacaoController` recebe as credenciais e as repassa para o `UsuarioService`.
3.  O `UsuarioService` busca o usuário no banco de dados pela matrícula (`NomeUsuario`).
4.  Se o usuário é encontrado, o serviço criptografa a `Senha` recebida e a compara com a senha já criptografada no banco de dados.
5.  Se as senhas corresponderem, a API gera um **token JWT**.
6.  O token é retornado ao cliente com `Status 200 OK`.

**Etapa 2: Acesso a Endpoints Protegidos**

1.  Para cada requisição a um endpoint protegido (ex: `GET /api/automacao/ativas`), o cliente deve incluir o token JWT no cabeçalho `Authorization`.
2.  O formato do cabeçalho deve ser: `Authorization: Bearer {seu_token}`.
3.  O middleware de autenticação da API valida o token (assinatura, data de validade, etc.). Se válido, a requisição prossegue. Caso contrário, retorna `Status 401 Unauthorized`.

### Claims do Token JWT

Quando um token é gerado, ele contém as seguintes *claims* (informações) sobre o usuário, que podem ser utilizadas pela aplicação:

| Claim | Exemplo | Descrição |
| :--- | :--- | :--- |
| `codigo` | "123" | ID único do usuário no banco de dados. |
| `nome` | "Nome Sobrenome" | Nome completo do usuário. |
| `email` | "usuario@snc.com" | Endereço de e-mail do usuário. |
| `matricula` | "f123456" | Matrícula do usuário, usada como nome de login. |

---

## 3. Endpoints da API

A URL base para todos os endpoints é `/api`.

### Autenticação (`AutenticacaoController`)

Este controller é público e serve como porta de entrada para a autenticação.

#### **Obter Token de Autenticação**
*   **Endpoint:** `POST /api/autenticacao/autenticar`
*   **Autorização:** Nenhuma.
*   **Descrição:** Autentica um usuário com base na matrícula e senha e retorna um token JWT.
*   **Request Body (`AutenticarRequest`):**
    ```json
    {
      "nomeUsuario": "sua-matricula",
      "senha": "sua-senha"
    }
    ```
*   **Resposta (200 OK - `AutenticarResponse`):**
    ```json
    {
      "sucesso": true,
      "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
      "nomeUsuario": "sua-matricula",
      "nomeExibicao": "Seu Nome Completo",
      "mensagem": "Autenticação realizada com sucesso."
    }
    ```

---

### Gerenciamento de Automações (`AutomacaoController`)

Todos os endpoints neste controller exigem autenticação (`[Authorize]`).

#### **Listar Todas as Automações**
*   **Endpoint:** `GET /api/automacao`
*   **Descrição:** Retorna uma lista de todas as automações, incluindo ativas e inativas.

#### **Listar Automações Ativas**
*   **Endpoint:** `GET /api/automacao/ativas`
*   **Descrição:** Retorna uma lista de todas as automações que estão com status "Ativo".

#### **Consultar Automação por Código**
*   **Endpoint:** `GET /api/automacao/{codigoAutomacao}`
*   **Descrição:** Retorna os detalhes de uma automação específica, incluindo seus parâmetros e ambientes.

#### **Listar Parâmetros de uma Automação**
*   **Endpoint:** `GET /api/automacao/{identificador}/parametros`
*   **Descrição:** Retorna a lista de parâmetros configurados para uma automação, com base em seu identificador.

#### **Listar Ambientes de uma Automação**
*   **Endpoint:** `GET /api/automacao/{identificador}/ambientes`
*   **Descrição:** Retorna a lista de ambientes de execução (URLs base) associados a uma automação.

#### **Listar Tipos de Parâmetros**
*   **Endpoint:** `GET /api/automacao/tipos-parametros`
*   **Descrição:** Retorna todos os tipos de parâmetros disponíveis no sistema (ex: texto, inteiro, arquivo).

#### **Criar Nova Automação**
*   **Endpoint:** `POST /api/automacao`
*   **Descrição:** Cria uma nova automação, seus parâmetros e vincula ambientes.
*   **Request Body (`CriarAutomacaoRequest`):** Veja a seção de DTOs.

#### **Atualizar Automação**
*   **Endpoint:** `PUT /api/automacao/{codigoAutomacao}`
*   **Descrição:** Atualiza os dados de uma automação e sincroniza seus ambientes.
*   **Request Body (`AtualizarAutomacaoRequest`):** Veja a seção de DTOs.

#### **Adicionar Parâmetro a uma Automação**
*   **Endpoint:** `POST /api/automacao/{codigoAutomacao}/parametros`
*   **Descrição:** Adiciona um novo parâmetro a uma automação já existente.
*   **Request Body (`CriarParametroRequest`):** Veja a seção de DTOs.

#### **Atualizar Parâmetro**
*   **Endpoint:** `PUT /api/automacao/parametros/{codigoParametro}`
*   **Descrição:** Atualiza os dados de um parâmetro existente.
*   **Request Body (`AtualizarParametroRequest`):** Veja a seção de DTOs.

#### **Excluir Parâmetro**
*   **Endpoint:** `DELETE /api/automacao/parametros/{codigoParametro}`
*   **Descrição:** Realiza uma exclusão lógica de um parâmetro (define seu status como inativo).

#### **Executar Automação (JSON)**
*   **Endpoint:** `POST /api/automacao/executar`
*   **Descrição:** Enfileira uma automação para execução em background. O nome do usuário que solicita a execução é extraído do token JWT e gravado para fins de auditoria.
*   **Request Body (`ExecutarAutomacaoRequest`):**
    ```json
    {
      "identificador": "minha-automacao",
      "parametros": {
        "parametro1": "valor1",
        "parametro2": "123"
      }
    }
    ```

#### **Executar Automação (com Arquivos)**
*   **Endpoint:** `POST /api/automacao/executar/com-arquivos`
*   **Descrição:** Enfileira uma automação para execução, permitindo o upload de arquivos (`multipart/form-data`). O nome do usuário também é extraído do token para auditoria.
*   **Form Fields:**
    *   `identificador` (string): Identificador da automação.
    *   `jsonParametros` (string, opcional): Parâmetros não-arquivo em formato JSON.
    *   `arquivos` (file, opcional): Um ou mais arquivos.
*   **Mapeamento de Arquivos:** A API tenta associar os arquivos enviados aos parâmetros do tipo `file` da automação. A lógica de mapeamento é:
    1.  Se houver apenas 1 parâmetro de arquivo e 1 arquivo enviado, eles são associados.
    2.  Caso contrário, a API tenta encontrar um arquivo cujo nome contenha o nome do parâmetro (ex: parâmetro `relatorio` e arquivo `relatorio_final.xlsx`).
    3.  Se nenhuma associação for feita, o primeiro arquivo enviado é associado ao primeiro parâmetro de arquivo como um fallback.

---

### Gerenciamento de Ambientes (`AmbienteExecucaoController`)

Endpoints para gerenciar os ambientes de execução.

#### **Listar Ambientes de Execução Ativos**
*   **Endpoint:** `GET /api/ambienteexecucao`
*   **Descrição:** Retorna uma lista de todos os ambientes de execução que estão com status "Ativo".

---

### Execução e Histórico (`ExecucoesController`)

Endpoints para consultar o andamento, histórico e arquivos de saída das execuções.

#### **Consultar Status da Execução**
*   **Endpoint:** `GET /api/execucoes/{id}`
*   **Descrição:** Retorna os detalhes completos de uma execução específica.

#### **Listar Todas as Execuções**
*   **Endpoint:** `GET /api/execucoes`
*   **Descrição:** Retorna uma lista paginada de todas as execuções.
*   **Query Parameters:** `pagina` (int), `tamanhoPagina` (int).

#### **Listar Execuções por Automação**
*   **Endpoint:** `GET /api/execucoes/automacao/{identificador}`
*   **Descrição:** Retorna o histórico paginado de execuções para uma automação específica.
*   **Query Parameters:** `pagina` (int), `tamanhoPagina` (int).

#### **Listar Arquivos de Saída de uma Execução**
*   **Endpoint:** `GET /api/execucoes/{execucaoId}/arquivos`
*   **Descrição:** Retorna a lista de metadados dos arquivos gerados por uma execução.

#### **Gerar Token para Download de Arquivo**
*   **Endpoint:** `GET /api/execucoes/{execucaoId}/arquivos/{arquivoId}/download-token`
*   **Descrição:** Gera um token JWT de curta duração (5 minutos) que autoriza o download de um arquivo de saída específico.

#### **Download de Arquivo com Token**
*   **Endpoint:** `GET /api/execucoes/download`
*   **Autorização:** Nenhuma (usa o token da query string).
*   **Descrição:** Realiza o download de um arquivo de saída. Requer um token válido gerado pelo endpoint anterior.
*   **Query Parameters:** `token` (string).

---

## 4. Modelos de Dados (DTOs)

A comunicação com a API é feita através de DTOs (Data Transfer Objects), a maioria localizada no projeto `SNC_HubAutomacoes.Shared`.

### `ApiResponse<T>`
Wrapper padrão para a maioria das respostas da API.
```csharp
public class ApiResponse<T>
{
    public bool Sucesso { get; set; }
    public string Mensagem { get; set; }
    public T? Dados { get; set; }
    public int StatusCode { get; set; }
}
```

### `AutomacaoDto`
Representa uma automação com seus parâmetros e ambientes.
```csharp
public class AutomacaoDto
{
    public int Codigo { get; set; }
    public string Identificador { get; set; }
    public string Nome { get; set; }
    public string Descricao { get; set; }
    public DateTime DataCadastro { get; set; }
    public DateTime DataAlteracao { get; set; }
    public StatusDto? Status { get; set; }
    public List<ParametroDto> Parametros { get; set; }
    public List<AmbienteExecucaoDto> Ambientes { get; set; }
}
```

### `ExecucaoDto`
Representa o registro completo de uma execução.
```csharp
public class ExecucaoDto
{
    public int Id { get; set; }
    public string? JobId { get; set; }
    public int AutomacaoId { get; set; }
    public string AutomacaoIdentificador { get; set; }
    public string AutomacaoNome { get; set; }
    public string Status { get; set; }
    public string? Mensagem { get; set; }
    public string? Erro { get; set; }
    public int? CodigoSaida { get; set; }
    public string? ParametrosJson { get; set; }
    public string? ArquivosJson { get; set; }
    public string? Saida { get; set; }
    public DateTime DataInicio { get; set; }
    public DateTime? DataFim { get; set; }
    public double? TempoExecucaoSegundos { get; set; }
    public string? Usuario { get; set; }
    public string? IpOrigem { get; set; }
}
```

### `ExecucaoIniciadaDto`
Representa a resposta retornada imediatamente após o enfileiramento de uma nova execução de automação.
```csharp
public class ExecucaoIniciadaDto
{
    public int ExecucaoId { get; set; }
    public string? JobId { get; set; }
    public string Identificador { get; set; }
    public string Status { get; set; }
    public string Mensagem { get; set;
    public DateTime DataSolicitacao { get; set; }
    public string? Usuario { get; set; }
}
```

### `ArquivoExecucaoDto`
Representa um arquivo de saída gerado por uma execução.
```csharp
public class ArquivoExecucaoDto
{
    public int Id { get; set; }
    public string Nome { get; set; }
    public string Caminho { get; set; }
    public string TipoMime { get; set; }
    public DateTime DataCriacao { get; set; }
}
```

---

## 5. Status Codes e Respostas

A API utiliza os seguintes status codes HTTP para indicar o resultado das operações:

| Código | Significado | Descrição |
| :--- | :--- | :--- |
| **200 OK** | Sucesso | A requisição foi bem-sucedida. Usado para `GET` e `PUT`. |
| **201 Created** | Criado | O recurso foi criado com sucesso. Usado para `POST` que criam um novo recurso. |
| **202 Accepted** | Aceito | A requisição foi aceita para processamento assíncrono (ex: enfileirar uma automação). |
| **400 Bad Request** | Requisição Inválida | A requisição está malformada, com dados ausentes ou inválidos. |
| **401 Unauthorized** | Não Autorizado | Autenticação necessária. O token JWT não foi fornecido ou é inválido. |
| **404 Not Found** | Não Encontrado | O recurso solicitado (ex: uma automação) não existe. |
| **500 Internal Server Error** | Erro Interno | Ocorreu um erro inesperado no servidor. |