# Bundled metadata reader

Echo's macOS package includes these unmodified upstream components, built from pinned source archives by `scripts/build-metadata-tool.sh`:

| Component | Version | Source |
| --- | --- | --- |
| ExifTool | 13.59 | <https://github.com/exiftool/exiftool/tree/13.59> |
| Perl | 5.42.3 | <https://www.cpan.org/src/5.0/perl-5.42.3.tar.gz> |

ExifTool is copyright Phil Harvey and contributors; Perl is copyright Larry Wall and contributors. Their license texts are shipped in `Contents/MacOS/cli/metadata/licenses` alongside the bundled reader. Original source archives are included in `metadata/sources`.

Echo invokes ExifTool as a separate process. The launcher selects Echo's bundled Perl runtime and modules; no user installation of Perl, ExifTool or Homebrew is required. The metadata build checks source archive SHA-256 hashes and rejects dependencies on non-system dynamic libraries.

Other .NET dependencies retain their upstream notices in the published application. See the project files and NuGet package metadata for their versions and licenses.
