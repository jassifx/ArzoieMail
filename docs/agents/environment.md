# Environment

The machine, tools and services ArzoieMail work runs on. BUILDING.md has the full build and test
instructions; this file covers what an agent needs to know on top of them.

## Development machine

- Windows Server 2025, Visual Studio 2026 (toolset v145, ATL, Spectre libraries), Strawberry Perl,
  Python 3, Inno Setup 6.7 (per-user install).
- The account is **not elevated**. Anything that needs elevation (registering the COM server,
  service permissions) is for the maintainer to run.
- `%hMailServerLibs%` points at the folder with every third-party library already built
  (OpenSSL, Boost, libpq, MariaDB Connector/C, 7-Zip, the .NET runtime installer).
- **ArzoieMail is not installed on this machine**, so there is no service and no registered COM
  server, and the regression tests can't run locally. They run on CI.
- The maintainer's editor keeps an untracked `.vscode/` folder in the clone. Leave it alone and
  never commit it.
- Shells: PowerShell and Git Bash are both available. The build scripts must run under
  `powershell.exe -NoProfile -ExecutionPolicy Bypass -File <script>`.

## Building

| What | Command | Time |
|---|---|---|
| Server | `build/build.ps1 -Configuration Release` (Debug is the default; CI builds Release) | ≈10 min |
| Tools and tests | MSBuild against the branch's own type library (below) | ≈1–2 min |

The tools and the tests compile against the COM type library. On this machine it isn't
registered, so pass the one the server build just wrote:

```
msbuild "<repo>\hmailserver\source\Tools\hMailServer Tools.sln" /restore /m /v:minimal ^
  /p:Configuration=Release /p:Platform=x64 ^
  /p:HMailServerTypeLibrary=<repo>\hmailserver\source\Server\hMailServer\x64\Release\hMailServer.tlb
```

and the same for `hmailserver\test\hMailServer Tests.sln`. `build\Find-MsBuild.ps1` locates
MSBuild. Build the server first: the type library comes from it.

## CI (GitHub Actions)

| Check | What it does | Required |
|---|---|---|
| `Release x64 (windows-2025-vs2026)` | Server, tools, tests and installer build | ✅ |
| `gcc`, `clang` | The portable core on Linux (`portable-core.yml`) | ✅ |
| `Tests / Regression tests` | Regression suite on the built-in database | ✅ |
| `Tests (PostgreSQL) / Regression tests` | The same on PostgreSQL | |
| `Tests (MySQL) / Regression tests` | The same on MySQL (run as a service) | |
| `Publish the installer` | Runs on `master` after the tests | |

Required means required by the `protect-master` ruleset; the maintainer's account can bypass
it. A full run takes 30–60 minutes. The NUnit totals are in each test job's log as the
`Test Count:` line; ROADMAP.md has the current baseline.

## GitHub without the `gh` CLI

There is no `gh` CLI here. [`tools/gh.py`](tools/gh.py) uses GitHub's REST API with the token
Git Credential Manager already holds (`git credential fill` for `github.com`); nothing is stored.

```
python docs/agents/tools/gh.py checks <sha>                      # check results of a commit
python docs/agents/tools/gh.py wait <sha>                        # wait until all checks finish
python docs/agents/tools/gh.py log <sha> "<check name>" <file>   # download a job's log
python docs/agents/tools/gh.py pr "<title>" <branch> <body.md>   # open a pull request on master
python docs/agents/tools/gh.py merge <number> <branch>           # merge, then delete the branch
```

- Pass the **full** commit SHA.
- Run `wait` in the background; it polls every 90 seconds for up to three hours.
- A job log is a redirect to blob storage, which must be followed **without** the Authorization
  header. Logs of older runs can be gone ("The specified blob does not exist").
- Write PR bodies and commit messages to files and pass the file; see gotchas.md.

## Hyper-V test VM

`test/VMTestRunner.Console` installs a real installer on a clean Windows and runs the regression
suite there (ROADMAP.md §6). The maintainer's host has:

- a Windows Server 2025 VM on an internal switch with NAT, restored from the checkpoint **Ready**
  before each run;
- its own DNS server inside the VM, because DNS blacklists refuse queries from public resolvers;
- setup scripts and VM credentials kept **outside** the repository. Ask the maintainer for them;
  never write them into the repository.

The VM catches what CI can't: CI's runner has the full Visual C++ redistributable, so a DLL the
installer forgets to ship only shows up on a clean machine.
