# Third-party code and licensing

SpiralLoader's original code, tests, examples, and documentation are offered under the GNU Affero General Public License, version 3 only (AGPL-3.0-only), unless a file or component says otherwise.

## Imlight patch series

`integration/imlight/patches/` contains patches for [Revive101/Imlight](https://github.com/Revive101/Imlight), an AGPL-3.0 project. The patch files preserve upstream copyright and license notices present in the modified source. When applying or redistributing the resulting Imlight source, retain the notices and comply with the applicable Imlight license. SpiralLoader does not claim ownership of upstream Imlight code.

## Dependencies

NuGet packages are declared in project files and restored during builds; this repository does not vendor their package source. Each package remains under the license published by its authors. Check the restored package metadata and the package's upstream license before redistributing a build that includes it. The Akka.Remote security advisory affecting the audited Imlight revisions is recorded in [the security policy](SECURITY.md) and [the dependency audit](docs/audit.md).

## Referenced projects and excluded material

[Spiral Shuffle](https://github.com/ljste/spiral-shuffle) is acknowledged as an independent project whose technical needs informed compatibility research; its source and assets are not included here, and no integration commitment is implied. No proprietary client files, game assets, extracted game data, or client-derived Imcodec generator inputs are distributed by this repository.

For SpiralLoader contributions, see [CONTRIBUTING.md](CONTRIBUTING.md). Preserve third-party attribution and licensing when adding dependencies or derived code; do not apply SpiralLoader's default license to material you do not own.
