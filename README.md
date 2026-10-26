# Roslyn Architecture Guidelines Analyzer

This repository contains a C# Roslyn analyzer for the architecture and coding
guidelines described by its diagnostic IDs.

| ID | Guideline |
| --- | --- |
| RAG001 | Avoid `global::`-qualified names |
| RAG002 | Avoid local functions |
| RAG003 | Avoid `ConfigureAwait(false)` |
| RAG004 | Keep one declared type per file |
| RAG005 | Remove unused using directives and sort the remaining directives |
| RAG006 | Do not leave catch blocks empty |
| RAG007 | Use centralized logging instead of `System.Diagnostics` or `Console.WriteLine` |
| RAG008 | Inline methods whose only action is returning a new object |
| RAG009 | Do not use optional constructor parameters or fallback object creation |
| RAG010 | Keep services focused on one distinct return-type contract |
| RAG011 | Do not inject exposure-layer types into services |
| RAG012 | Keep exposure types free of fields, properties, and primary-constructor state |
| RAG013 | Keep service constructors limited to dependency assignment |

Build the analyzer with `dotnet build Analyzer/Analyzer.csproj` and run its
tests with `dotnet test Analyzer.Tests/Analyzer.Tests.csproj`. The diagnostics
are warnings and can be configured in `.editorconfig` by their `RAG` IDs.

Create a NuGet package with `dotnet pack Analyzer/Analyzer.csproj`. The
`Publish NuGet package` GitHub Actions workflow tests, packs, and publishes
the package to GitHub Packages whenever a GitHub Release is published. The
release tag supplies the package version, with an optional leading `v`
removed (for example, `v1.2.3` publishes version `1.2.3`).

The workflow uses the repository's `GITHUB_TOKEN` to publish. Add a
`GH_PACKAGES_TOKEN` Actions secret containing a classic personal access token
with `write:packages` scope; the workflow uses it through GitHub CLI to set the
package visibility to **Public** after publishing. The token owner must have
permission to manage the package (and, for an organization, any required SSO
authorization). Add the resulting `RoslynArchitectureGuidelines` package to
a consuming project to run the analyzer during builds.

Service and exposure rules identify types by convention: a service is in a
`Services` namespace or has a name ending in `Service`; an exposure type is in
an `Exposures` namespace or has a name ending in `Exposure`.
# callum-analyzer
