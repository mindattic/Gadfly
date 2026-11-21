# Sample: a third-party LMS using the hosted Gadfly API

A console app that plays an LMS against a running **Gadfly.Server** with an organization **API key**
(follows [docs/API.md](../../docs/API.md)). Against a server in mock AI mode it costs nothing.

It creates a collection, uploads a markdown source, processes it, creates and publishes an exam, assigns a
student, gets the student's signed launch link, registers a webhook endpoint **that the sample itself
hosts** (Kestrel, HTTPS), and verifies the HMAC signature (`X-Gadfly-Signature`) of the
`session.completed` delivery it receives, then reads the result. `WebhookVerifier.cs` is the
verification, and is the part to copy into your own app.

## 1. Start Gadfly.Server in mock mode

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Gadfly__AiMode = 'mock'                              # no provider keys, no spend
$env:Gadfly__Webhooks__AllowPrivateTargets = 'true'       # Development only: lets the server call https://localhost
dotnet run --project Gadfly.Server -- --seed-demo         # --seed-demo must be the last argument
```

(Use `$env:ConnectionStrings__Gadfly` to point the server at a scratch database instead of the default
LocalDB `Gadfly` one.) Production refuses webhook URLs that are not public `https` hosts, so a real LMS
registers its real endpoint; the flag above exists only so the sample can receive deliveries locally.

## 2. Provision an API key

An API key belongs to one organization and acts for all of it. Keys are made by an organization **Owner**
or **Admin**:

- **In the console:** sign in as the organization owner, open **API keys** (`/org/api-keys`), choose
  **Create a key**, name it ("LMS integration"). The full key (`gfk_<8 hex>_<secret>`) is shown **once**;
  store it in a secret manager. Only its SHA-256 is kept, so a lost key is replaced, never recovered.
  See [Admin guide, API keys](../../docs/guides/ADMIN_GUIDE.md#api-keys).
- **For a local trial:** `--seed-demo` creates the "Gadfly Demo Academy" organization with an owner and a
  student; set `Gadfly__Demo__ApiKey` (a `gfk_...` string you choose) and `Gadfly__Demo__Password` first to
  pin the key and the accounts' password, otherwise they are generated and printed in the server log once.

```powershell
$env:Gadfly__Demo__ApiKey   = 'gfk_5a3b1e77_aLongRandomSecretOfYourChoosing0123456789'
$env:Gadfly__Demo__Password = 'A-long-passphrase-of-your-choosing-2026!'
```

## 3. Run the sample

```powershell
cd samples/HostedApi
$env:GADFLY_URL     = 'https://localhost:7311'            # where Gadfly.Server listens
$env:GADFLY_API_KEY = 'gfk_5a3b1e77_aLongRandomSecretOfYourChoosing0123456789'
dotnet run -- --student demo.student --student-password $env:Gadfly__Demo__Password
```

Options: `--url`, `--api-key` (instead of the variables), `--student` (a user name; unknown names create a new
Student account and print a one-time temporary password), `--student-password`, `--hook-port` (default
7401), `--hook-public-url` (register this URL instead of `https://localhost:<hook-port>/hooks/gadfly`, for
example a tunnel to your machine), `--wait-minutes` (default 10).

With **`--student-password`** the sample plays the student's browser too: it signs in, opens the launch
link, and answers the exam over the exam room's SessionHub, so the whole run, including the webhook,
needs no human. That part is a stand-in for a real browser and not part of the API contract. Without it
the sample prints the launch link and waits for you to open it in a browser, sign in as the student, and
take the exam. (A brand-new student must first change their temporary password at first sign-in, so
for the unattended run use an existing student such as the demo's `demo.student`.)

The dev certificate must be trusted (`dotnet dev-certs https --trust`), both for the sample's webhook
endpoint (the server's delivery client validates it) and for Gadfly.Server itself.

## What to take into your own app

- Send `Authorization: Bearer gfk_...` on every call; the key is the organization, not a person.
- Ask for a **fresh launch link** each time the student clicks "Start exam" (it expires in 15 minutes).
- Verify every webhook against the **raw** body with the whole `whsec_...` secret, reject timestamps more
  than five minutes off, answer `2xx` quickly, and treat repeated `X-Gadfly-Delivery` ids as duplicates.
- The AI result is a **recommendation**; the instructor's grade (`result.grade`) comes later.
