# ArzoieMail release identity and version policy

Decision: **2026-10-28, maintainer instruction**. ArzoieMail follows its own release trajectory.
The next product release target is **ArzoieMail 6.0.0**, replacing the previous 5.8.0 target.
The implementation and architecture have diverged; upstream hMailServer numbering no longer
defines ArzoieMail's new releases. This is a development target, not a release announcement,
publication instruction or claim that Phase 1b/other roadmap requirements are complete.

## Version ownership and generated metadata

| Value | Owner / format | Meaning |
|---|---|---|
| Next product target | Root [VERSION](../VERSION): `6.0.0` | Canonical three-part major.minor.patch release version |
| Server-reported version | `6.0.0-B<build>` | Native service/COM/API status reports use the stamped product version and build |
| Native PE and shipped desktop tool file/product versions | `6.0.0.<build>` | Windows resources, file properties and signing metadata |
| Installer filename | `ArzoieMail-6.0.0-B<build>-x64.exe` | Product/version/build/architecture identity; existing installer AppId remains stable |
| Installer display and version properties | `ArzoieMail 6.0.0-B<build>-x64`, numeric `6.0.0.<build>` | Installer/Windows version metadata; not the database schema version |
| Release tag/title | `ArzoieMail-6.0.0-b<build>` / `ArzoieMail 6.0.0, build <build>` | Full three-part ArzoieMail version; manual release workflow creates a draft only when explicitly requested |
| Management API product metadata | VersionPrefix read from `VERSION` | SDK-generated file/product metadata follows the product target; preview default build component is zero |
| .NET assembly identity / COM typelib | Existing identity versions retained | Internal compatibility/binding contracts; a 1.x/5.x assembly identity is not a 5.x product-release target |
| Database schema | `REQUIRED_DB_VERSION`, currently **5714** | Independent migration sequence; do not change it merely because product major becomes 6 |

[Set-Version.ps1](../build/Set-Version.ps1) stamps the native header, shipped desktop-tool
file/informational versions, generated legacy PHP version gate and installer setup metadata.
Development metadata is committed at the target version with **build zero**. CI stamps a unique
build number using its existing offset/run-number policy; the major-version change does not
reset that sequence. The API reads its product prefix from VERSION and keeps assembly identity
`1.0.0.0`; shipped desktop tools likewise retain existing assembly identities while their
file/informational versions follow the stamp. Internal IDs are not marketing versions.

The generated `WebAdmin/include_versioncheck.php` is ignored build output, not one of the 89
maintained PHP source files. It remains necessary for legacy PHP compatibility until cutover;
its generation/packaging must be retired with that application. Inventory checks exclude
ignored outputs and include tracked/nonignored new sources.

## Building and maintenance

1. Change VERSION for a newly authorized product target. Update this policy, README, roadmap,
   build examples, release metadata expectations and the latest handoff together.
2. On the development host, generate development metadata without executing the product:

   ```powershell
   $releaseVersion = (Get-Content VERSION -Raw).Trim()
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File build/Set-Version.ps1 $releaseVersion 0
   ```

3. Review generated diffs. Do not change assembly/COM identity, installer AppId, service names,
   schema numbers or historical SQL filenames as a side effect. API metadata derives its prefix
   directly from VERSION; production API build-number/signing integration belongs to hosting/
   installer delivery and must be specified there rather than inferred from the preview.
4. Compile on the host with service hooks disabled. Run executables, installer/upgrade tests,
   previews and regressions only in the disposable VM or CI. Verify reported/PE versions,
   displayed installer/tool versions, database-upgrade labels and existing-install compatibility.
5. Require the four database jobs, other project CI gates and maintainer review. A successful
   build/artifact upload does not publish a production release. Release creation/signing and
   public publication need the maintainer's explicit release instruction and acceptance scope.

Manual workflow version overrides remain available for controlled historical/reproduction
builds; do not use a 5.8.x override as the new ArzoieMail release target. Future maintenance
branches follow ArzoieMail releases, for example `6.0.0` or `6.0.1`, not an assumed upstream
roadmap. Branch names and release tags must keep the workflow's supported syntax.

## Compatibility and provenance

- Preserve the identifiers listed in [AGENTS](../AGENTS.md): `hMailServer.exe`, service name,
  COM ProgID/type library, INI, installer AppId, database tables, backup names, headers and
  source/project filenames. The major-version decision does not authorize renaming them.
- Preserve historical hMailServer release/schema mappings, SQL upgrade chains, original
  authorship and AGPLv3/license notices. ArzoieMail remains derived from that code; independent
  release ownership does not erase provenance or licensing obligations.
- Schema 5714's current ArzoieMail display label is `ArzoieMail 6.0 (5714)`. The older
  `hMailServer 5.8 (5713)` label is a legacy mapping, not the current product target.
- Keep older handoffs, test logs, manual examples and third-party dependency versions factual.
  Do not globally replace `5.8`: it can occur in historical data or unrelated library versions.
- The roadmap still owns phase order. This version decision does not automatically schedule
  every future phase into 6.0.0 or waive Phase 1b migration, security and capacity gates.
