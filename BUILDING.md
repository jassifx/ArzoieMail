Building ArzoieMail
===================

How to build ArzoieMail from source, run it under the debugger, test it and release it. For
what the project is and where it is going, see the [README](README.md).


## 6.0 migration and release policy

The commands below describe current code, including COM registration/interop not yet removed.
The [approved scope](docs/version-6-scope.md) requires all phases, complete COM retirement and
API migration of tools/tests before the integrated validation baseline. Runtime execution
stays VM/CI-only. CI test installers remain allowed; no development previews before feature
completion. Official production releases require explicit approval, Authenticode-signed shipped
executables/setup, checksums and provenance verification. SQLite will replace Compact in normal
regression lanes; retain separate Compact migration/recovery checks. See the [migration register](docs/breaking-changes-6.0.md).


Branches
--------

   * The master branch contains the latest development version of ArzoieMail. It is protected:
     changes reach it through a pull request, and only once the build and the regression tests
     have passed on GitHub Actions.

   * Work happens on a branch named after what it is for, such as `feature/...`, `bugfix/...` or
     `techdebt/...`. GitHub Actions builds and tests every push to branches with those prefixes.

   * A version branch, named after the version (for example 6.0.1), can be cut from master for
     bug fixes to a released version.

Product version and release identity
------------------------------------

The next release target is **ArzoieMail 6.0.0**, independently versioned from hMailServer.
[VERSION](VERSION) is canonical. [Release versioning](docs/release-versioning.md) documents
the server/tool/installer stamp, API metadata, build numbering, current assembly/COM identities (COM retires under the approved 6.0 migration)
and schema independence. Development metadata is `6.0.0` with build zero; CI stamps its unique
build. To refresh development metadata before compiling, without running the product:

```powershell
$releaseVersion = (Get-Content VERSION -Raw).Trim()
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build/Set-Version.ps1 $releaseVersion 0
```

Product 6 does not renumber the database schema (now 5715) or remove legacy upgrade paths. Runtime validation
stays in the disposable VM/CI. Artifact publication is not authorization to publish a release.
Root `global.json` selects the stable .NET 10 SDK and permits newer compatible feature bands
within 10. The [CI acceptance matrix](docs/ci-acceptance-matrix.md) describes each modern-stack
test layer, published API/browser checks, actual HTTPS/native integration, retained evidence
and the separate deployment/capacity/release lanes still required.

Environment set up
------------------

**Required software**

   * Visual Studio 2026 Community edition with the C++ desktop workload, ATL and the
     Spectre-mitigated libraries (see below)
   * Inno Setup 6.7.3 (https://github.com/jrsoftware/issrc/releases/tag/is-6_7_3)
   * Perl 5 (https://strawberryperl.com/) - required by the OpenSSL and PostgreSQL library builds
   * Python 3 (https://www.python.org/downloads/) - required by the PostgreSQL library build
   * CMake (https://cmake.org/download/) - unless Visual Studio's "C++ CMake tools for Windows" component is installed
   * To run the regression tests: an installed ArzoieMail (or hMailServer 5.7 or later),
     configured with a database. The tests drive it through its COM API.

**NOTE**

Do not build ArzoieMail on a computer that runs a production mail server. Building stops the
running ArzoieMail (or hMailServer) service and registers the newly built hMailServer.exe as the service and COM
server on the machine, so a production installation stops running. If that happens, reinstall the
production version.

Installing Visual Studio
------------------------

ArzoieMail's C++ projects are built as C++20 with the v145 platform toolset, the default
compiler of Visual Studio 2026. They link ATL statically and are compiled with Spectre
mitigations, so ATL and the Spectre-mitigated libraries have to be selected during installation.

**Visual Studio 2026**

1. Download [Visual Studio 2026](https://visualstudio.microsoft.com/vs/) and launch the installation.
2. Select the following _Workloads_
  * .NET desktop development
  * Desktop development with C++
3. Select the following _Individual components_
  * C++ ATL for latest v145 build tools (x86 & x64)
  * MSVC v145 - C++ x64/x86 Spectre-mitigated libs (Latest)
  * C++ ATL for latest v145 build tools with Spectre Mitigations (x86 & x64)

The build scripts locate the installed Visual Studio automatically.

3rd party libraries
-------------------

The 3rd party libraries ArzoieMail links against are large and updated often, so they are not
checked into this repository. They are downloaded and built by the scripts in `libraries\`, one
per library, described below. The build finds them through a system environment variable named
hMailServerLibs.

Create an environment variable named hMailServerLibs pointing at a folder where you will store
the libraries, such as C:\Dev\hMailLibs. Build OpenSSL first: libpq and MariaDB Connector/C link
against it.

The versions the project is pinned to can be listed with:

   <pre>
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\Get-LibraryVersions.ps1
   </pre>

Building OpenSSL
----------------
OpenSSL 3.5.x is built by the `libraries\build-openssl.ps1` script, which downloads the
requested version into %hMailServerLibs%\openssl-&lt;Version&gt; and builds it into an `out64`
install prefix (headers, import libs and `libcrypto-3-x64.dll` / `libssl-3-x64.dll`).

Prerequisites:
- The environment variable hMailServerLibs (see above).
- Perl (e.g. [Strawberry Perl](https://strawberryperl.com/)) on PATH - required by OpenSSL's Configure.
- Visual Studio 2026 with the x64 C++ build tools (the script locates vcvars64.bat automatically).

Run, from the repository root:

   <pre>
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File libraries\build-openssl.ps1 -Version 3.5.9
   </pre>

Only OpenSSL 3.5.x, the current long-term support release, is supported; the build recipe for
3.0.x and 4.x differs.

Building PostgreSQL
-------------------
ArzoieMail talks to PostgreSQL through libpq. libpq is built by the `libraries\build-pgsql.ps1`
script, which downloads the requested version into %hMailServerLibs%\postgresql-&lt;Version&gt;,
configures it with Meson against a previously built OpenSSL, and builds `libpq.dll` / `libpq.lib`
into `postgresql-&lt;Version&gt;\Release\libpq`. The script fetches pinned copies of Meson, Ninja
and winflexbison (flex and bison) into `%hMailServerLibs%\tools` itself.

Prerequisites:
- The environment variable hMailServerLibs (see above).
- A matching OpenSSL build (`openssl-&lt;Version&gt;\out64`) already present - build it first with the OpenSSL script above.
- Perl (e.g. [Strawberry Perl](https://strawberryperl.com/)) on PATH - required by PostgreSQL's build.
- Python 3 on PATH - Meson runs on it.
- Visual Studio 2026 with the x64 C++ build tools.

Run, from the repository root:

   <pre>
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File libraries\build-pgsql.ps1 -Version 18.6
   </pre>

The script auto-detects the OpenSSL version to link against from the project; pass
`-OpenSSLVersion 3.5.x` to override it. Only PostgreSQL 17.x and 18.x are supported. libpq from
PostgreSQL 16 and later requires Windows 10, which is why ArzoieMail does too.

Building MariaDB Connector/C
----------------------------
ArzoieMail talks to MySQL and MariaDB through MariaDB Connector/C, and ships its
`libmariadb.dll` in the Bin directory. It is built by the `libraries\build-mariadb.ps1` script,
which downloads the requested version into %hMailServerLibs%\libmariadb-&lt;Version&gt;, configures it
with CMake against a previously built OpenSSL, and builds
`libmariadb.dll` into `libmariadb-&lt;Version&gt;\build64\libmariadb\RelWithDebInfo`. The authentication
plugins are compiled into the DLL, so no plugin files have to be shipped alongside it.

Prerequisites:
- The environment variable hMailServerLibs (see above).
- A matching OpenSSL build (`openssl-&lt;Version&gt;\out64`) already present - build it first with the OpenSSL script above.
- CMake - either on PATH, or Visual Studio's "C++ CMake tools for Windows" component. MariaDB Connector/C has no other build system.
- Visual Studio 2026 with the x64 C++ build tools.

Run, from the repository root:

   <pre>
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File libraries\build-mariadb.ps1 -Version 3.4.11
   </pre>

The script auto-detects the OpenSSL version to link against from the project; pass
`-OpenSSLVersion 3.5.x` to override it. Only MariaDB Connector/C 3.4.x is supported.

Building Boost
--------------
ArzoieMail links statically against a handful of compiled Boost libraries (thread, filesystem,
regex, chrono, atomic; Boost.System is header-only as of Boost 1.92). Boost is built by the
`libraries\build-boost.ps1` script, which
downloads the requested version into %hMailServerLibs%\boost_&lt;Version&gt; (e.g. `boost_1_92_0`),
bootstraps `b2`, and builds the static, multithreaded x64 libraries into `stage\lib`.

Prerequisites:
- The environment variable hMailServerLibs (see above).
- Visual Studio 2026 with the x64 C++ build tools (the script locates vcvars64.bat
  automatically and drives `b2` with the msvc-14.5 toolset, which is the toolset ArzoieMail
  itself is built with).

Run, from the repository root:

   <pre>
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File libraries\build-boost.ps1 -Version 1.92.0
   </pre>

Pass `-Toolset &lt;name&gt;` to override the default `msvc-14.5`, or `-Jobs &lt;n&gt;` to change the
number of parallel compilations (defaults to the number of logical processors). Only Boost 1.x is
supported.

Fetching 7-Zip
--------------
The backup and restore feature launches the 7-Zip command line tool to compress and read
backup archives. It is a prebuilt binary rather than something ArzoieMail compiles, so the
`libraries\build-7zip.ps1` script downloads the "7-Zip Extra" package into
%hMailServerLibs%\7zip-&lt;Version&gt; (e.g. `7zip-26.03`) and keeps the x64 `7za.exe` and its
LGPL license text.

Prerequisites:
- The environment variable hMailServerLibs (see above).

Run, from the repository root:

   <pre>
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File libraries\build-7zip.ps1 -Version 26.03
   </pre>

The package is a .7z, so the script also downloads `7zr.exe` from the same release and uses
it to unpack the archive.

Both downloads are checked against SHA-256 values pinned in the script, so only versions
listed there are accepted. To move to a newer 7-Zip, record its hashes in `$ArchiveHashes`
and `$BootstrapHashes` and update the `7zip-&lt;Version&gt;` folder name in `post-build.bat` and
`hMailServer64.iss`.

Fetching the .NET Desktop Runtime
---------------------------------
The administration tools are .NET 10 applications, and the installer bundles Microsoft's .NET
Desktop Runtime installer, which it runs when the runtime is missing, so that an installation
needs no internet connection. The `libraries\fetch-dotnet-runtime.ps1` script downloads it into
%hMailServerLibs%\dotnet-desktop-runtime-&lt;Version&gt; and checks it against the SHA-512 that
Microsoft publishes, pinned in the script.

Run, from the repository root:

   <pre>
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File libraries\fetch-dotnet-runtime.ps1 -Version 10.0.13
   </pre>

To move to a newer runtime, record its hash in `$RuntimeHashes` and update
`DOTNET_DESKTOP_RUNTIME_VERSION` in `hMailServer64.iss`.

Building the server, the tools and the installer
------------------------------------------------

The scripts under `build\` locate Visual Studio themselves. Run them from the repository root
with `powershell.exe -NoProfile -ExecutionPolicy Bypass -File <script>`:

1. `build\build.ps1` builds the server, `hmailserver\source\Server\hMailServer\hMailServer.sln`
   (hMailServer.exe). Pass `-Configuration Release` for a release build; the default is Debug.
2. `build\post-build.ps1` stops the service, copies the library DLLs beside the new
   hMailServer.exe, registers it as the COM server if needed, and starts the service again.
   Only the registration needs elevation. Run `build\grant-service-control.ps1` once, elevated,
   to let your account start and stop the service without a UAC prompt.
3. Build `hmailserver\source\Tools\hMailServer Tools.sln` in Visual Studio, or with
   `dotnet build "hmailserver\source\Tools\hMailServer Tools.sln" -c Release -p:Platform=x64`.
   This builds ArzoieMail Administrator, Database Setup, the database upgrader and the other
   tools, which are .NET 10 Windows Forms applications (the .NET 10 SDK comes with Visual
   Studio 2026). They generate their COM interop from the type library the server build writes,
   using TlbImp from the .NET Framework 4.8 tools of the Windows SDK, so build the server first.
4. Compile `hmailserver\installation\hMailServer64.iss` with InnoSetup to build the installer,
   `ArzoieMail-<version>-B<build>-x64-Setup.exe`.

   The installer ships the newest installed Visual C++ runtime beside hMailServer.exe. Point
   the environment at it first:

   ```
   . build\Get-VCRedistPath.ps1
   $env:VCRedistPath = Get-VCRedistPath -Newest
   ```

   Windows 10 and later carry the Universal CRT themselves, so it is not shipped.

GitHub Actions does all of the above on every push to master and to the branch prefixes listed
under Branches, then installs the result on a clean runner and runs the regression tests against
it on SQL Server Compact, PostgreSQL, MySQL and SQL Server 2025 Express
(`.github/workflows/build.yml` and `tests.yml`). See the [CI test inventory](docs/ci-test-inventory.md)
for the check list, current baseline, management/browser fixture boundaries and manual suites.

Running in Debug
----------------

To run the server in debug mode in Visual Studio, add the command argument /debug. You find this
setting in the Project properties, under Configuration Properties -> Debugging.
Execute/debug the server only in the disposable VM; the development host is for editing,
static inspection and compilation.

Running tests
-------------

The regression tests exercise the server end to end over SMTP, IMAP and POP3, and through its
COM API. They are NUnit tests in `hmailserver\test\RegressionTests`. Every bug fix and every new
feature needs a regression test.

Run all runtime tests only in the disposable VM or CI, including tests launched from an IDE.
The tests reconfigure that VM's server: existing domains and accounts are deleted and test
accounts are created. Preserve/restore its checkpoint and keep credentials out of the repository.

1. Make sure the matching server is installed, registered and running inside the disposable VM.
2. Build the test solution with `build\build-tests.ps1`.
3. Run the tests with the NUnit console runner from the restored packages, for example:

   ```
   hmailserver\test\packages\nunit.consolerunner\<version>\tools\nunit3-console.exe hmailserver\test\RegressionTests\bin\x64\Debug\RegressionTests.dll
   ```

Inside the test VM, you can also open `hmailserver\test\hMailServer Tests.sln` in Visual Studio
and run tests from Test Explorer, under "RegressionTests". Compilation on the development host
does not authorize test execution there.

Code signing
============

ArzoieMail builds are not code-signed yet. `build.yml` can sign through
[SignPath](https://signpath.io) when a manual run asks for it, but the SignPath organization and
project it names are hMailServer's, so signing an ArzoieMail build needs its own SignPath project
and the `SIGNPATH_*` settings in the workflow pointed at it.

Releasing ArzoieMail
====================

Before a release, without finding any serious issues:

1. Run the regression tests on the supported versions of Windows and with each supported
   database, using the VM test runner (`hmailserver\test\VMTestRunner.Console`). This includes
   the environments that upgrade an existing hMailServer installation.
2. Run the server stress tests.
3. Run the page heap tests to check for memory issues, using the VM test runner:
   `--test "Windows 10 - Page heap: Regression tests"`.

Then create the release from the GitHub Actions build of the commit being released: run the
Release workflow (`.github/workflows/release.yml`) with its build number. It attaches the
installer and the symbols to a draft GitHub release, which is reviewed, given release notes and
published by hand.


## SQL Server / Express validation

See [SQL Server deployment and migration](docs/sql-server.md) for OLE DB 19 prerequisites,
required encryption, certificate validation, endpoint/authentication settings and the disposable
SQL Server 2025 Express fixture. SQL Server Compact is a separate backend. Compile on the
development host; run all connection probes and regressions only inside the VM or CI.
