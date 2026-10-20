# Security policy

SpiralLoader is experimental and is not presented as production-ready. Native mods execute in the server process with that process's permissions; the loader does not sandbox them.

## Reporting a vulnerability

Please report suspected vulnerabilities privately through GitHub's **Report a vulnerability** feature for this repository, if it is enabled. If that option is unavailable, contact the repository maintainers through their GitHub profile and request a private channel. Do not include exploit details, credentials, or private server data in a public issue.

Include the affected commit or version, the .NET and Imlight revisions, deployment context, and a concise reproduction where safe. Do not attach game assets, client-derived inputs, user data, or live credentials.

## Known upstream dependency advisory

The Imlight revisions currently covered by the integration use Akka.Remote versions older than 1.5.52. GitHub advisory [GHSA-jhpv-4q4f-43g5](https://github.com/advisories/GHSA-jhpv-4q4f-43g5), CVE-2025-61778, rates the issue critical and describes missing client-certificate enforcement for TLS-enabled Akka.Remote connections. Akka.NET lists 1.5.52 as the patched version. The advisory states that deployments not using TLS or restricted to a private network they fully control are not affected by this specific scenario; operators should still assess their own configuration and plan an upstream dependency update. SpiralLoader has not silently suppressed this advisory or independently fixed Imlight's package versions.

See [the Imlight source audit](docs/audit.md) for the versions observed in the supported revisions and [the upstream advisory](https://github.com/advisories/GHSA-jhpv-4q4f-43g5) for current details.
