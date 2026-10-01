# Streaming Panel

Painel de catálogo de **Filmes** e **Álbuns** com autenticação JWT, autorização por papéis
e um painel administrativo. Backend em **ASP.NET Core 9** + **PostgreSQL 16**; frontend em
**Angular 20**.

A API expõe catálogos de filmes e álbuns com capas, CRUD administrativo de pessoas/mídias
e um fluxo de autenticação com rotação de refresh token. O frontend consome essa API com
guardas de rota, interceptor de autenticação, sidebar dinâmico por papel, spinner global e
notificações.

## Stack

| Camada     | Tecnologia                                                        |
|------------|-------------------------------------------------------------------|
| Backend    | ASP.NET Core 9, EF Core 9, FluentValidation, JWT                  |
| Banco      | PostgreSQL 16 (via Docker)                                        |
| Frontend   | Angular 20 (standalone components, signals)                       |
| Infra      | Docker Compose (API + Postgres + pgAdmin)                         |
| Testes     | xUnit + CsCheck/FsCheck (backend); Jasmine/Karma + fast-check (front) |

## Arquitetura

O backend segue uma arquitetura em camadas (inspirada em Clean Architecture) com o fluxo
de dependências sempre apontando para o centro — a camada de domínio não conhece infra nem web:

```
StreamingPanel.Api  ──►  StreamingPanel.Infrastructure  ──►  StreamingPanel.Core
   (web/HTTP)                (EF Core, serviços)                (domínio puro)
```

- **Core** — o coração, sem dependência de framework de dados ou web. Contém as entidades,
  os DTOs, os enums, as interfaces (contratos de repositórios e serviços) e os validators
  (FluentValidation). Define também os tipos `Result`/`Result<T>` e `PagedResult<T>` usados
  para comunicar sucesso/erro sem lançar exceção no fluxo esperado.
- **Infrastructure** — implementa os contratos do Core: `AppDbContext` e mapeamentos EF Core,
  repositórios, serviços de aplicação (Auth, Movie, Album, Person), segurança (hashing de
  senha, geração de JWT) e o seed inicial do banco.
- **Api** — a borda HTTP: controllers finos que validam a entrada, chamam os serviços e
  traduzem `Result` em status HTTP; middleware de erro (RFC 7807); e a composição de DI,
  autenticação/autorização, CORS e Swagger.

Princípios que orientam o código:
- **Dependency Inversion** — serviços dependem de interfaces (`IMovieRepository`,
  `IPasswordHasher`, etc.), não de implementações; a composição acontece no `Program.cs`.
- **Single Responsibility** — controllers só orquestram HTTP; serviços concentram a regra
  de negócio; repositórios só acessam dados.
- **Erros como valor** — o fluxo esperado usa `Result`/`ErrorCode` em vez de exceções; o
  middleware converte falhas inesperadas em respostas Problem Details sem vazar stack trace.

O frontend espelha essa separação com a convenção **core / shared / features**:
- **core** — serviços singleton, guards, interceptors e models que sustentam o app inteiro.
- **shared** — componentes reutilizáveis de apresentação (grid, card, modal, sidebar, etc.).
- **features** — telas por domínio (auth, movies, albums, admin), cada uma com suas rotas.

## Estrutura de pastas

```
.
├─ docker-compose.yml              # Orquestra API + Postgres + pgAdmin
├─ Dockerfile                      # Imagem da API (.NET 9, multi-stage)
├─ .env.example                    # Modelo das variáveis de ambiente (copie para .env)
├─ global.json                     # Fixa o SDK .NET 9
├─ StreamingPanel.sln
├─ Streaming Panel.postman_collection.json   # Coleção Postman (Auth/Persons/Movies/Albums)
├─ Streaming Panel.postman_environment.json  # Ambiente Postman (baseUrl + tokens)
│
├─ src/
│  ├─ StreamingPanel.Core/         # Domínio (sem dependência de EF/web)
│  │  ├─ Entities/                 # Person, Address, Phone, Movie, Album, RefreshToken
│  │  ├─ Dtos/                     # Requests/responses + Result<T> e PagedResult<T>
│  │  ├─ Enums/                    # Role
│  │  ├─ Interfaces/               # Contratos de repositórios e serviços
│  │  └─ Validation/               # Validators FluentValidation
│  │
│  ├─ StreamingPanel.Infrastructure/
│  │  ├─ Persistence/              # AppDbContext, Configurations (Fluent API), Seed
│  │  ├─ Repositories/             # Acesso a dados por agregado
│  │  ├─ Services/                 # Regras de negócio (Auth, Movie, Album, Person)
│  │  ├─ Security/                 # PasswordHasher (PBKDF2), JwtTokenGenerator
│  │  └─ Migrations/               # Migrations EF Core (geradas)
│  │
│  └─ StreamingPanel.Api/
│     ├─ Controllers/              # Endpoints finos + base com mapeamento Result→HTTP
│     ├─ Extensions/               # Setup de Auth, CORS, Swagger e startup do banco
│     ├─ Middleware/               # Tratamento de erro RFC 7807
│     └─ Program.cs                # Composition root (DI + pipeline)
│
├─ tests/
│  └─ StreamingPanel.Tests/        # xUnit + testes baseados em propriedades
│
└─ frontend/
   ├─ proxy.conf.json              # Encaminha /api → http://localhost:8080 no dev server
   └─ src/
      ├─ environments/             # environment.ts (apiBaseUrl)
      └─ app/
         ├─ core/
         │  ├─ guards/             # authGuard, roleGuard
         │  ├─ interceptors/       # authInterceptor (bearer + refresh), spinnerInterceptor
         │  ├─ models/             # DTOs do front, Role, ProblemDetails, PagedResult
         │  └─ services/           # Token, Auth, Movies, Albums, Spinner, Notification, Breakpoint
         ├─ shared/
         │  └─ components/         # catalog-grid, media-card, details-modal, sidebar,
         │                         #   layout-shell, spinner-overlay, toast-container, unauthorized
         └─ features/
            ├─ auth/               # Tela de login + rotas
            ├─ movies/             # Catálogo de filmes
            ├─ albums/             # Catálogo de álbuns
            └─ admin/              # Painel administrativo (CRUD de pessoas/filmes/álbuns)
```

## Pré-requisitos

- **Docker Desktop** (para o backend + banco)
- **Node.js 20+** e **npm** (para o frontend)
- Opcional: **.NET SDK 9** se quiser rodar o backend ou os testes fora do Docker

## Como rodar

O backend sobe inteiro via Docker; o frontend roda com o dev server do Angular.

### 1. Backend (Docker)

Na raiz do projeto:

```bash
# 1. Crie o arquivo de ambiente a partir do exemplo
cp .env.example .env           # Windows PowerShell: Copy-Item .env.example .env

# 2. Edite o .env e defina pelo menos:
#    DB_PASSWORD        (senha do banco)
#    JWT_SIGNING_KEY    (chave aleatoria, >= 32 caracteres)
#    PGADMIN_PASSWORD   (senha do pgAdmin)
#    PGADMIN_EMAIL      use um dominio valido (ex.: admin@example.com) -
#                       o pgAdmin rejeita dominios reservados como .local
#    Para desenvolvimento, deixe ASPNETCORE_ENVIRONMENT=Development
#    (habilita o Swagger UI).

# 3. Suba a stack (compila a API, sobe Postgres + pgAdmin,
#    aplica migrations e roda o seed)
docker compose up --build
```

Depois de subir:

| Serviço     | URL                                                     |
|-------------|---------------------------------------------------------|
| API         | http://localhost:8080                                   |
| Swagger     | http://localhost:8080/swagger (apenas em Development)   |
| pgAdmin     | http://localhost:5050                                   |

> Rode o `docker compose` **na raiz** (onde está o `docker-compose.yml`), não dentro de `frontend/`.
> Se as portas 8080 / 5050 estiverem ocupadas, ajuste `API_HOST_PORT` / `PGADMIN_HOST_PORT` no `.env`.

### 2. Frontend (Angular)

Em **outro terminal** (o frontend não faz parte do compose):

```bash
cd frontend
npm install
npx ng serve --proxy-config proxy.conf.json
```

Aguarde o `Application bundle generation complete` e acesse **http://localhost:4200**.
Deixe esse terminal aberto — ele é o dev server.

> O `proxy.conf.json` encaminha as chamadas `/api/*` do frontend (porta 4200) para a API
> (porta 8080). Sem ele o Angular não encontra o backend.
>
> Na primeira execução o Angular CLI pode perguntar sobre telemetria — responda `N`.
> Para silenciar permanentemente: `npx ng analytics disable`.

## Login / Papéis

O seed cria apenas o usuário **Admin**. As três contas de usuário comum abaixo foram
criadas para teste (via `POST /api/auth/register`). Você também pode criar novas pessoas
pelo painel administrativo logado como Admin.

| Papel        | Email                          | Senha        | Acesso no app                     |
|--------------|--------------------------------|--------------|-----------------------------------|
| **Admin**    | `admin@streamingpanel.local`   | `Admin123!`  | Filmes, Álbuns e Administração     |
| User_Movie   | `movie@example.com`            | `Teste123!`  | Apenas Filmes                     |
| User_Album   | `album@example.com`            | `Teste123!`  | Apenas Álbuns                     |
| User_Full    | `full@example.com`             | `Teste123!`  | Filmes e Álbuns (sem admin)       |

Matriz de autorização:

| Papel        | Filmes | Álbuns | Painel Admin |
|--------------|:------:|:------:|:------------:|
| Admin        |   ✅   |   ✅   |      ✅      |
| User_Full    |   ✅   |   ✅   |      ❌      |
| User_Movie   |   ✅   |   ❌   |      ❌      |
| User_Album   |   ❌   |   ✅   |      ❌      |

Ao logar, o sidebar mostra apenas os menus permitidos para o papel, e um botão de **Logout**
para trocar de conta. Tentar acessar uma rota não permitida (ex.: `/admin` como `User_Movie`)
é bloqueado pelo guard e redireciona.

### Criar os usuários de teste manualmente (se o banco for recriado)

Com o backend no ar, registre cada papel:

```bash
curl -X POST http://localhost:8080/api/auth/register ^
  -H "Content-Type: application/json" ^
  -d "{\"email\":\"movie@example.com\",\"password\":\"Teste123!\",\"role\":\"User_Movie\",\"name\":\"User Movie\"}"
```

Repita trocando `role`/`email` por `User_Album` / `album@example.com` e
`User_Full` / `full@example.com`. Papéis válidos: `Admin`, `User_Movie`, `User_Album`, `User_Full`.

## Testes

```bash
# Backend (na raiz)
dotnet test StreamingPanel.sln

# Frontend (em frontend/)
npx ng test --watch=false --browsers=ChromeHeadless
```

## Explorando a API

- **Swagger UI**: http://localhost:8080/swagger — clique em **Authorize** e cole o
  `accessToken` retornado por `/api/auth/login` para chamar os endpoints protegidos.
- **Postman**: importe `Streaming Panel.postman_collection.json` e
  `Streaming Panel.postman_environment.json` (na raiz). O login/refresh já captura os
  tokens automaticamente no ambiente.
- **pgAdmin** (http://localhost:5050): login com `PGADMIN_EMAIL` / `PGADMIN_PASSWORD`
  do `.env`. Para conectar ao banco, use host `postgres`, porta `5432`, e as credenciais
  `POSTGRES_USER` / `DB_PASSWORD`.

## Parar / limpar

```bash
docker compose down        # para os containers
docker compose down -v     # para e apaga os dados do Postgres (recria o seed no próximo up)
```

## Notas

- As migrations e o seed rodam automaticamente no startup da API (com retry limitado
  enquanto o Postgres aquece).
- As capas são servidas por um endpoint autenticado; o frontend as busca via HttpClient
  (passando o token) e exibe, em vez de usar `<img src>` direto.
- Segredos ficam no `.env`, que está no `.gitignore` e não é versionado. Os valores do
  `.env.example` são apenas um modelo — não use em produção.